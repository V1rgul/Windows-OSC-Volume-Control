# Windows-OSC-Volume-Control

Windows tray application that captures configurable global hotkeys and maps them to **OSC** mixer actions (tested on X32). It is built for fast desktop control without keeping the mixer UI in focus.

![Configuration window](doc/screenshot.png)

## Features

- **System tray app**: Tray icon with menu (configure, exit) and live status icon state.
- **Global hotkeys**: Uses a low-level keyboard hook so configured shortcuts work system-wide, not only when a window is focused.
- **Fader control bindings**: Map keys (default: Volume Down / Volume Up) to OSC fader addresses with per-binding step size and min/max clamp.
- **Toggle control bindings**: Map keys (default: Volume Mute) to OSC float toggles (typical use: mute on/off).
- **Multiple mappings**: Configure multiple independent fader and toggle bindings, each with its own hotkey and OSC address.
- **On-screen status (OSD)**: Shows pending, level, toggle, and error feedback for hotkey actions; size and display duration are configurable.
- **Connection health feedback**: Startup connection test and runtime failure detection update tray state and surface errors.
- **Connection settings**: Configure OSC target IP, port, and query timeout in the settings window, with connectivity checks.
- **Optional autostart**: Register/unregister in the configuration window with dynamic current-state feedback.
- **Resilient config persistence**: Stores settings in `%APPDATA%` and falls back to defaults for missing/invalid entries.

## Typical use cases

- Drive one or more mixer faders (or knobs) from configurable hotkeys (including media keys).
- Bind dedicated hotkeys for talkback, mute (groups), or scene-related toggle parameters.
- Keep audio control available while working in other applications.

## Notes

- Platform: Windows (WPF + Fluent theme + low-level keyboard hook).
- Mixer compatibility: implemented against OSC and tested on Behringer X32; other OSC mixers may work if value semantics match.
- OSC library: `src/Osc/SharpOSC` vendor code is based on [ValdemarOrn/SharpOSC](https://github.com/ValdemarOrn/SharpOSC).

## Running a Release build

- Install: [.NET 10 Desktop Runtime (Windows)](https://dotnet.microsoft.com/download/dotnet/10.0)
- Choose installer architecture matching the target OS (`x64`, `x86`, or `Arm64`).
- No additional VC++ redistributable is required by this project.

## Building

- This project is built with **Visual Studio** (open `Windows-OSC-Volume-Control.slnx`, then build `Debug` or `Release`).
- The **.NET 10 SDK** is required: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Testing

- Unit tests use **xUnit**.
- Run them with `dotnet test "tests/WindowsOscVolumeControl.Tests/WindowsOscVolumeControl.Tests.csproj"`.
