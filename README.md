# InfoPanel (maintained fork)

<p align=center>
  <img src="Images/logo.png" width=60/>
</p>

<p align=center>Hardware monitoring panels for your desktop, external monitors and USB LCD screens, driven by HWiNFO and LibreHardwareMonitor. This is a maintained fork of <a href="https://github.com/habibrehmansg/infopanel">InfoPanel</a> with support for many more USB displays and a steady stream of fixes.</p>

<br />

[Releases][release] | [Discord][discord] | [Issues][issues] | [User Guide](docs/USER_GUIDE.md) | [Panel Guide](PANELS.md) | [InfoPanel for Linux][linux]

![build status](https://github.com/emaspa/infopanel-1/actions/workflows/dotnet-desktop.yml/badge.svg?branch=all-changes)

## What this is

InfoPanel is a Windows app that turns sensor readings into custom dashboards. You design a layout of text, gauges, graphs, bars and images, then show it as a desktop overlay, on a spare monitor, or on a small USB LCD panel such as the ones built into many coolers and cases.

This repository is a fork of [habibrehmansg/infopanel][upstream], the original project by Habib Rehman. It contains all of the upstream code plus the changes listed below. Profiles, settings and plugins are compatible in both directions, so you can switch between the two without losing your work.

## Why this fork exists

The original project has had no commits since April 2026, and over twenty pull requests are waiting there without review. Meanwhile, new USB displays keep shipping, and existing ones need protocol fixes that only get found by people running them.

This fork keeps InfoPanel moving: it merges the pending work, adds support for new panels, and fixes bugs reported by users. If upstream becomes active again, changes from here can be offered back.

## What is different from upstream

**More USB displays**
- Thermalright: Trofeo Vision 9.16" and 11.3", Elite Vision 360 ARGB Black, Wonder Vision 360 v2
- Jonsbo: DS916, DS339 and other MacroSilicon MS9132-based displays, driven at the resolution the panel reports
- Lian Li LCD panels
- Jungle Leopard / Hongtai cooler displays
- VMAX 4.6" display over direct USB

**Fixes**
- Frame size cap for HID Trofeo panels, which stops them freezing
- Startup timeouts on Jonsbo DS339
- Profile windows that could not be dragged under heavy load
- Plugin actions going to a stale host connection

**Features**
- Thresholds on bars and graphs, and automatic scaling of sensor values and units
- Multi-select editing: duplicate, reorder, delete, group by dragging, and axis lock with Shift
- Display assignment in profile settings
- Configurable refresh interval for URL images
- Metric or imperial units in the Weather plugin
- Bundled Stopwatch plugin with global hotkeys, and an OBS Monitor plugin
- Newer LibreHardwareMonitor, including NVIDIA hotspot sensors
- A [User Guide](docs/USER_GUIDE.md) linked from the Home page

## Getting it

Download the latest build from the [releases page][release]. The Microsoft Store and infopanel.net versions are the original upstream builds and do not include the changes above.

Found a bug or have a panel that does not work? Open an [issue][issues]. For panels that are not detected, a log from startup helps a lot. For questions and help with setting up your panels, join the [Discord][discord].

On Linux, use [InfoPanel for Linux][linux], a native port that shares profiles and plugins with this fork.

## Features

- **Multiple Data Sources**: 
  - HWiNFO integration via Shared Memory (SHM) for extensive hardware monitoring
  - LibreHardwareMonitor for additional sensor data without requiring HWiNFO
  - Extensible plugin system with built-in and third-party plugins

- **Display Options**:
  - Desktop overlay with customizable transparency and positioning
  - External display support for monitors
  - USB LCD panel support including BeadaPanel via WinUSB API
  - Multiple visualization types: text, gauges, graphs, bars, donuts, and images

- **Advanced Visualization**:
  - GIF animation support for dynamic visualizations
  - High refresh rates for smooth updates
  - Customizable layouts, colors, and fonts
  - Multiple profiles for different use cases or displays

- **Plugin System**:
  - Built-in system information plugins (CPU, memory, network, drives)
  - Weather information integration
  - Support for third-party plugins
  - Sensor data tables with customizable formatting

![InfoPanel](./Images/infopanel-design-view.png)

## Supported Hardware

- All hardware sensors exposed by HWiNFO
- CPU, GPU, RAM, storage, and network monitoring via LibreHardwareMonitor
- USB LCD panels from BeadaPanel, Turing Smart Screen/Turzx, Thermalright, Jonsbo, Lian Li, Jungle Leopard and more (see the [Panel Guide](PANELS.md))
- TuringPanel/TURZX displays (Models A, C, and E)
- Any standard monitor or display

For detailed information about supported panels and recommendations, see our [Display Panels Guide](PANELS.md).

## Usage

1. Install either HWiNFO (with Shared Memory support enabled) or use the built-in LibreHardwareMonitor integration
2. Download InfoPanel from the [releases page][release]
3. Configure your display profile with sensors, gauges, and visualizations
4. Customize layouts with drag-and-drop interface
5. Connect USB displays or position on your desktop
6. (Optional) Install additional plugins for enhanced functionality

## Plugins

InfoPanel features a robust plugin system that extends its capabilities:

### Built-in Plugins
- **System Info Plugin**: CPU usage, memory usage, process statistics, and system uptime
- **Network Info Plugin**: Network interfaces, IP addresses, and connection statistics
- **Drive Info Plugin**: Storage device information and usage statistics
- **Volume Plugin**: Audio volume control and monitoring
- **Weather Plugin**: Current weather conditions and forecasts

### Community Plugins
- [InfoPanel Spotify Plugin](https://github.com/F3NN3X/InfoPanel.Spotify) - Displays currently playing tracks and album art from Spotify
- [InfoPanel FPS Plugin](https://github.com/F3NN3X/InfoPanel.FPS) - Shows FPS and performance metrics for gaming sessions
- [InfoPanel YouTube Live Plugin](https://github.com/fweepa/InfoPanel.YoutubeLivePlugin) - Plays a YouTube Live stream in your panel

To browse all available community plugins or submit your own, see the [Plugin Registry](PLUGIN-REGISTRY.md).

### Plugin Development
InfoPanel provides a comprehensive API for plugin development that allows access to:
- Sensor data creation and publishing
- Custom visualizations
- Data tables for complex information
- Configuration interfaces

For detailed instructions on developing plugins, see our [Plugin Development Guide](PLUGINS.md).

## Demo
![InfoPanel Demo](./Images/beadapanel-demo-1.gif)

*A demonstration of InfoPanel in action on a BeadaPanel USB LCD*

## Development

InfoPanel is built with C# and WPF for a modern Windows UI experience. The architecture features:

- Modular design with MVVM pattern
- Extensible plugin system
- DirectX acceleration for UI elements
- High-performance graphics rendering for external displays
- Cross-device synchronization

## Credits

InfoPanel was created by [Habib Rehman](https://github.com/habibrehmansg). This fork is maintained by [emaspa](https://github.com/emaspa). Thanks to everyone who contributed panel support, plugins and bug reports.

## License

InfoPanel is licensed under GPL 3.0 - see the [license file][license] for details.

---

InfoPanel is not affiliated with HWiNFO. HWiNFO is a registered trademark of its respective owners.

<!--
References
-->

[release]: https://github.com/emaspa/infopanel-1/releases
[issues]: https://github.com/emaspa/infopanel-1/issues
[discord]: https://discord.gg/aNGeJxjE7Q
[upstream]: https://github.com/habibrehmansg/infopanel
[linux]: https://github.com/emaspa/InfoPanel-linux
[license]: https://github.com/emaspa/infopanel-1/blob/all-changes/LICENSE
