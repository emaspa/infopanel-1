using HidSharp;
using LibUsbDotNet;
using LibUsbDotNet.Main;
using Serilog;
using System;
using System.Linq;
using System.Threading;

namespace InfoPanel.JonsboPanel
{
    /// <summary>
    /// MacroSilicon MS9132 USB display transport, used by the Jonsbo DS339 (376x960).
    /// Protocol from the ms912x/ms9132 Linux drivers plus a USB capture of the OEM
    /// JONSBO-AIO app driving a real DS339 (see JONSBO-AIO PROTOCOL.md):
    ///
    ///   * Control plane rides the HID interface (MI_00) as 8-byte feature reports:
    ///       A6 [op] [6 bytes]  writes, B5 [addr_be16] reads (SET_REPORT then GET_REPORT).
    ///     Mode-set sequence (captured verbatim from the OEM app):
    ///       A6 07 01 02                    power on
    ///       A6 05 00                       video off
    ///       A6 03 03
    ///       A6 01 [w_be16] [h_be16] 11 00  input info: resolution, color=0x11 (RGB888), byte_sel=0
    ///       A6 02 [vic] 00 [w_be16] [h_be16]  output info: VIC index, out color 0
    ///       A6 04 01
    ///       A6 05 01                       video on
    ///   * Frames go to bulk OUT EP 0x04 on the vendor interface (MI_03, "msusb video",
    ///     needs the OEM's libusb driver or WinUSB):
    ///       8-byte header: FF 00 | x/16 (u8) | y (be16) | width/16 (u8) | height|0x8000 (be16)
    ///       + width*height*3 bytes BGR888 + 8-byte trailer FF C0 00 00 00 00 00 00,
    ///     written in 64 KB chunks. Width byte rounds down (376/16 -> 23), as captured.
    /// </summary>
    public sealed class JonsboMs9132Device : IDisposable
    {
        private static readonly ILogger Logger = Log.ForContext<JonsboMs9132Device>();

        public const int VENDOR_ID = 0x345F;
        public const int PRODUCT_ID = 0x9132;

        private const int ChunkSize = 65536;
        private const int WriteTimeoutMs = 5000;

        private HidStream? _hidStream;
        private UsbDevice? _usbDevice;
        private UsbEndpointWriter? _writer;
        private bool _disposed;

        public string SerialNumber { get; private set; } = string.Empty;

        public static JonsboMs9132Device? Open()
        {
            var dev = new JonsboMs9132Device();
            try
            {
                // Control plane: the HID interface, via the inbox HID driver.
                var hidDevice = DeviceList.Local.GetHidDevices(VENDOR_ID, PRODUCT_ID).FirstOrDefault();
                if (hidDevice == null)
                {
                    Logger.Warning("JonsboMs9132: HID interface not found (VID={Vid:X4} PID={Pid:X4})", VENDOR_ID, PRODUCT_ID);
                    dev.Dispose();
                    return null;
                }

                if (!hidDevice.TryOpen(out var hidStream))
                {
                    Logger.Warning("JonsboMs9132: Cannot open HID interface");
                    dev.Dispose();
                    return null;
                }
                dev._hidStream = hidStream;

                try { dev.SerialNumber = hidDevice.GetSerialNumber(); } catch { }

                // Frame pipe: the vendor "msusb video" interface via libusb/WinUSB.
                var finder = new UsbDeviceFinder(VENDOR_ID, PRODUCT_ID);
                dev._usbDevice = UsbDevice.OpenUsbDevice(finder);
                if (dev._usbDevice == null)
                {
                    Logger.Warning("JonsboMs9132: Video interface not found — is the Jonsbo MSUSBDisplay driver installed on interface 3?");
                    dev.Dispose();
                    return null;
                }

                if (dev._usbDevice is IUsbDevice wholeDevice)
                {
                    wholeDevice.SetConfiguration(1);
                    wholeDevice.ClaimInterface(3);
                }

                dev._writer = dev._usbDevice.OpenEndpointWriter(WriteEndpointID.Ep04);

                Logger.Information("JonsboMs9132: Opened (serial {Serial})", dev.SerialNumber);
                return dev;
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "JonsboMs9132: Open failed");
                dev.Dispose();
                return null;
            }
        }

        private void WriteControl(byte[] payload8)
        {
            if (_hidStream == null) throw new InvalidOperationException("HID not open");
            // Feature report: [reportId=0] + 8 payload bytes.
            var buffer = new byte[9];
            Array.Copy(payload8, 0, buffer, 1, Math.Min(payload8.Length, 8));
            _hidStream.SetFeature(buffer);
        }

        /// <summary>Reads a device register via B5 (SET_REPORT request, GET_REPORT reply).</summary>
        public int ReadRegister(ushort address)
        {
            if (_hidStream == null) throw new InvalidOperationException("HID not open");
            WriteControl([0xB5, (byte)(address >> 8), (byte)(address & 0xFF), 0, 0, 0, 0, 0]);
            var buffer = new byte[9];
            _hidStream.GetFeature(buffer);
            // Reply layout mirrors the request: [reportId] B5 addr_hi addr_lo data0 ...
            return buffer[4];
        }

        /// <summary>Panel connection status (register 0x32): 1 = connected.</summary>
        public bool IsPanelConnected()
        {
            try { return ReadRegister(0x0032) == 1; }
            catch { return false; }
        }

        /// <summary>
        /// Applies the captured OEM mode-set sequence for the given native resolution and VIC.
        /// </summary>
        public void SetMode(int width, int height, byte vic)
        {
            byte wHi = (byte)(width >> 8), wLo = (byte)(width & 0xFF);
            byte hHi = (byte)(height >> 8), hLo = (byte)(height & 0xFF);

            WriteControl([0xA6, 0x07, 0x01, 0x02, 0, 0, 0, 0]);   // power on
            WriteControl([0xA6, 0x05, 0x00, 0, 0, 0, 0, 0]);      // video off
            WriteControl([0xA6, 0x03, 0x03, 0, 0, 0, 0, 0]);
            WriteControl([0xA6, 0x01, wHi, wLo, hHi, hLo, 0x11, 0x00]); // in: RGB888
            WriteControl([0xA6, 0x02, vic, 0x00, wHi, wLo, hHi, hLo]);  // out: VIC
            WriteControl([0xA6, 0x04, 0x01, 0, 0, 0, 0, 0]);
            WriteControl([0xA6, 0x05, 0x01, 0, 0, 0, 0, 0]);      // video on
            Thread.Sleep(100);

            Logger.Information("JonsboMs9132: Mode set {Width}x{Height} VIC {Vic}", width, height, vic);
        }

        public void PowerOff()
        {
            try { WriteControl([0xA6, 0x07, 0, 0, 0, 0, 0, 0]); }
            catch (Exception ex) { Logger.Debug(ex, "JonsboMs9132: PowerOff failed"); }
        }

        /// <summary>
        /// Sends one full frame. <paramref name="bgrPixels"/> must be width*height*3 bytes,
        /// B,G,R order, top-down rows at the panel's native (portrait) resolution.
        /// </summary>
        public void SendFrame(byte[] bgrPixels, int width, int height)
        {
            if (_writer == null) throw new InvalidOperationException("USB not open");

            int payloadLen = width * height * 3;
            if (bgrPixels.Length < payloadLen)
                throw new ArgumentException($"Pixel buffer too small: {bgrPixels.Length} < {payloadLen}");

            int total = 8 + payloadLen + 8;
            var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(total);
            try
            {
                // Header: FF 00, x/16, y BE16, width/16 (rounded down, per OEM capture), height | 0x8000.
                buffer[0] = 0xFF;
                buffer[1] = 0x00;
                buffer[2] = 0x00;                       // x/16
                buffer[3] = 0x00;                       // y hi
                buffer[4] = 0x00;                       // y lo
                buffer[5] = (byte)(width / 16);
                int flaggedHeight = height | 0x8000;
                buffer[6] = (byte)(flaggedHeight >> 8);
                buffer[7] = (byte)(flaggedHeight & 0xFF);

                Buffer.BlockCopy(bgrPixels, 0, buffer, 8, payloadLen);

                // Trailer: FF C0 00 00 00 00 00 00
                int t = 8 + payloadLen;
                buffer[t] = 0xFF;
                buffer[t + 1] = 0xC0;
                for (int i = 2; i < 8; i++) buffer[t + i] = 0;

                // Write in 64 KB chunks, matching the OEM app's transfer pattern.
                int offset = 0;
                while (offset < total)
                {
                    int len = Math.Min(ChunkSize, total - offset);
                    var ec = _writer.Write(buffer, offset, len, WriteTimeoutMs, out int written);
                    if (ec != ErrorCode.None || written != len)
                        throw new Exception($"USB bulk write failed: {ec} ({written}/{len})");
                    offset += len;
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PowerOff();
            try { _hidStream?.Dispose(); } catch { }
            _hidStream = null;
            try
            {
                if (_usbDevice != null)
                {
                    if (_usbDevice.IsOpen)
                    {
                        if (_usbDevice is IUsbDevice wholeDevice)
                        {
                            try { wholeDevice.ReleaseInterface(3); } catch { }
                        }
                        _usbDevice.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug(ex, "JonsboMs9132: Dispose error");
            }
            _usbDevice = null;
            _writer = null;
        }
    }
}
