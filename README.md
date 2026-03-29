# Windows-OSC-Volume-Control

Windows tray application that intercepts the configurable keys and routes them to an **OSC** mixer over (tested only on X32). You can also bind other hotkeys to toggle OSC parameters and to nudge additional faders.

![Configuration window](doc/screenshot.png)

## Features

- **Fader bindings** — Map keys (by default Volume Down / Volume Up) to OSC fader paths with configurable step size and limits.
- **Toggle bindings** — Map a key (by default Volume Mute) to flip a float at an OSC address (e.g. mute).
- **OSC connection** — IP, port, and query timeout; settings UI includes connectivity checks.
- **Optional autostart** — Register with Windows startup from the tray menu.
- Settings persist next to the executable.

## Requirements

- Windows (WinForms, low-level keyboard hook).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build; runtime included when publishing a self-contained build if you prefer.
