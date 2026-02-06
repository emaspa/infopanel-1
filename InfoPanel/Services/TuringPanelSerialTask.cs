using InfoPanel.Extensions;
using InfoPanel.Models;
using InfoPanel.Utils;
using SkiaSharp;
using System;
using Serilog;
using System.Threading;
using System.Threading.Tasks;
using TuringSmartScreenLib;
using TuringSmartScreenLib.Helpers.SkiaSharp;
using System.Diagnostics;
using System.IO.Ports;
using InfoPanel.TuringPanel;

namespace InfoPanel
{
    public sealed class TuringPanelSerialTask : BackgroundTask
    {
        private static readonly ILogger Logger = Log.ForContext<TuringPanelSerialTask>();
        private readonly TuringPanelDevice _device;

        private readonly ScreenType _screenType;
        private readonly int _panelWidth;
        private readonly int _panelHeight;

        private readonly int _nativeWidth;
        private readonly int _nativeHeight;
        private readonly ScreenOrientation _screenOrientation;

        private readonly int _sectorWidth;
        private readonly int _sectorHeight;

        private readonly int _maxSectorWidth;
        private readonly int _maxSectorHeight;

        private readonly int _maxSectors;

        public TuringPanelSerialTask(TuringPanelDevice device)
        {
            _device = device;
            var modelInfo = device.ModelInfo;

            if (modelInfo != null)
            {
                _panelWidth = modelInfo.Width;
                _panelHeight = modelInfo.Height;
                _nativeWidth = modelInfo.Width;
                _nativeHeight = modelInfo.Height;
                _screenOrientation = ScreenOrientation.Portrait;

                switch (modelInfo.Model)
                {
                    case TuringPanel.TuringPanelModel.TURING_3_5:
                        _screenType = ScreenType.RevisionA;
                        _sectorWidth = 20;
                        _sectorHeight = 20;
                        _maxSectorWidth = 40;
                        _maxSectorHeight = 40;
                        _maxSectors = 76;
                        break;
                    case TuringPanel.TuringPanelModel.REV_5INCH:
                        _screenType = ScreenType.RevisionC;
                        _sectorWidth = 20;
                        _sectorHeight = 20;
                        _maxSectorWidth = 120;
                        _maxSectorHeight = 80;
                        _maxSectors = 30;
                        break;
                    case TuringPanel.TuringPanelModel.REV_8INCH:
                    case TuringPanel.TuringPanelModel.REV_2INCH:
                        _screenType = ScreenType.RevisionE;
                        _sectorWidth = 32;
                        _sectorHeight = 32;
                        _maxSectorWidth = 128;
                        _maxSectorHeight = 96;
                        _maxSectors = 38;
                        break;
                    case TuringPanel.TuringPanelModel.REV_13INCH_USB:
                        _screenType = ScreenType.RevisionE;
                        _nativeWidth = 440;
                        _nativeHeight = 1920;
                        _sectorWidth = 32;
                        _sectorHeight = 32;
                        _maxSectorWidth = 128;
                        _maxSectorHeight = 96;
                        _maxSectors = 38;
                        break;
                    default:
                        throw new ArgumentException($"Unsupported TuringPanel model: {modelInfo.Model}", nameof(device));
                }
            }
            else
            {
                throw new ArgumentException("Device model information is not available.", nameof(device));
            }
        }

        public SKBitmap? GenerateLcdBitmap()
        {
            if (ConfigModel.Instance.GetProfile(_device.ProfileGuid) is Profile profile)
            {
                var rotation = _device.Rotation;
                var bitmap = PanelDrawTask.RenderSK(profile, false);

                var ensuredBitmap = SKBitmapExtensions.EnsureBitmapSize(bitmap, _panelWidth, _panelHeight, rotation);

                if (!ReferenceEquals(bitmap, ensuredBitmap))
                {
                    bitmap.Dispose();
                }

                return ensuredBitmap;
            }

            return null;
        }

        /// <summary>
        /// For dual-port devices (CT13INCH), initialize the SoC control port
        /// to wake the CH340 data port.
        /// </summary>
        private void InitializeCompanionPort()
        {
            var companionPort = TuringPanelHelper.FindCompanionPort(0x0525, 0xa4a7);
            if (companionPort == null)
            {
                Logger.Warning("CT13INCH: Companion SoC port (0525:A4A7) not found");
                return;
            }

            Logger.Information("CT13INCH: Initializing via companion port {Port}", companionPort);
            try
            {
                using var port = new SerialPort(companionPort)
                {
                    DtrEnable = true,
                    RtsEnable = true,
                    ReadTimeout = 5000,
                    WriteTimeout = 5000,
                    BaudRate = 115200,
                    StopBits = StopBits.One,
                    Parity = Parity.None
                };
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                // Send Hello command to wake the device
                var hello = new byte[] { 0x01, 0xef, 0x69, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0xc5, 0xd3 };
                var buffer = new byte[250];
                Array.Copy(hello, buffer, hello.Length);
                port.Write(buffer, 0, 250);

                // Read response
                var readBuf = new byte[1024];
                try
                {
                    var read = port.Read(readBuf, 0, 24);
                    Logger.Information("CT13INCH: Companion port responded with {Length} bytes", read);
                }
                catch (TimeoutException)
                {
                    Logger.Warning("CT13INCH: Companion port read timeout");
                }

                port.Close();
                Logger.Information("CT13INCH: Companion port initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "CT13INCH: Failed to initialize companion port");
            }
        }

        protected override async Task DoWorkAsync(CancellationToken token)
        {
            await Task.Delay(300, token);
            try
            {
                // For CT13INCH: initialize via SoC port before opening CH340 data port
                if (_device.ModelInfo?.Model == TuringPanel.TuringPanelModel.REV_13INCH_USB)
                {
                    InitializeCompanionPort();
                    await Task.Delay(500, token); // Give the device time to wake
                }

                using var screen = ScreenFactory.Create(_screenType, _device.DeviceLocation, _nativeWidth, _nativeHeight);

                if (screen == null)
                {
                    Logger.Warning("TuringPanelE: Screen not found on port {Port}", _device.DeviceLocation);
                    return;
                }

                screen.Orientation = _screenOrientation;
                _device.UpdateRuntimeProperties(isRunning: true);

                screen.ScreenOn();
                var brightness = _device.Brightness;
                screen.SetBrightness((byte)brightness);

                SKBitmap? sentBitmap = null;

                try
                {
                    var fpsCounter = new FpsCounter();
                    var stopwatch = new Stopwatch();
                    var canDisplayPartialBitmap = false;

                    while (!token.IsCancellationRequested)
                    {
                        stopwatch.Restart();

                        if (brightness != _device.Brightness)
                        {
                            brightness = _device.Brightness;
                            screen.SetBrightness((byte)brightness);
                        }

                        var bitmap = GenerateLcdBitmap();

                        if (bitmap != null)
                        {
                            if (sentBitmap == null || !canDisplayPartialBitmap)
                            {
                                sentBitmap = bitmap;

                                canDisplayPartialBitmap = screen.DisplayBuffer(screen.CreateBufferFrom(sentBitmap));
                                //Trace.WriteLine($"Full sector update: {stopwatch.ElapsedMilliseconds}ms");
                            }
                            else
                            {
                                var sectors = SKBitmapComparison.GetChangedSectors(sentBitmap, bitmap, _sectorWidth, _sectorHeight, _maxSectorWidth, _maxSectorHeight);
                                //Trace.WriteLine($"Sector detect: {sectors.Count} sectors {stopwatch.ElapsedMilliseconds}ms");

                                if (sectors.Count > _maxSectors)
                                {
                                    canDisplayPartialBitmap = screen.DisplayBuffer(screen.CreateBufferFrom(bitmap));
                                    //Trace.WriteLine($"Full sector update: {stopwatch.ElapsedMilliseconds}ms");
                                }
                                else
                                {
                                    foreach (var sector in sectors)
                                    {
                                        canDisplayPartialBitmap = screen.DisplayBuffer(sector.Left, sector.Top, screen.CreateBufferFrom(bitmap, sector.Left, sector.Top, sector.Width, sector.Height));
                                    }

                                    //Trace.WriteLine($"Sector update: {stopwatch.ElapsedMilliseconds}ms");
                                }
                                sentBitmap?.Dispose();
                                sentBitmap = bitmap;
                            }
                        }

                        fpsCounter.Update(stopwatch.ElapsedMilliseconds);
                        _device.UpdateRuntimeProperties(frameRate: fpsCounter.FramesPerSecond, frameTime: fpsCounter.FrameTime);

                        var targetFrameTime = 1000.0 / ConfigModel.Instance.Settings.TargetFrameRate;
                        if (stopwatch.ElapsedMilliseconds < targetFrameTime)
                        {
                            var sleep = (int)(targetFrameTime - stopwatch.ElapsedMilliseconds);
                            //Trace.WriteLine($"Sleep {sleep}ms");
                            await Task.Delay(sleep, token);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    Logger.Debug("Task cancelled");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Exception during execution");
                }
                finally
                {
                    screen.ScreenOff();
                    sentBitmap?.Dispose();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Initialization error");
            }
            finally
            {

                _device.UpdateRuntimeProperties(isRunning: false);
            }
        }
    }
}
