using System.Drawing;
using System.Windows.Forms;

namespace X32VolumeHijacker;

public enum AppTrayIconState {
	StartingOrInvalidConfig,
	NetworkError,
	Ok,
}

/// <summary>Loads status icons from <c>Assets/Icon/app</c> next to the executable and applies them to the tray (and optionally the config form titlebar).</summary>
public sealed class AppIconController {
	readonly NotifyIcon _tray;
	Icon? _errorGlobal;
	Icon? _errorNetwork;
	Icon? _ok;

	public AppIconController(NotifyIcon tray) {
		_tray = tray;
		string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon", "app");
		_errorGlobal = LoadIcon(Path.Combine(dir, "error_global.ico"));
		_errorNetwork = LoadIcon(Path.Combine(dir, "error_network.ico"));
		_ok = LoadIcon(Path.Combine(dir, "ok.ico"));
	}

	static Icon LoadIcon(string path) {
		try {
			return new Icon(path);
		} catch {
			return SystemIcons.Application;
		}
	}

	public AppTrayIconState State { get; private set; } = AppTrayIconState.StartingOrInvalidConfig;

	Icon Resolve(AppTrayIconState state) => state switch {
		AppTrayIconState.Ok => _ok ?? SystemIcons.Application,
		AppTrayIconState.NetworkError => _errorNetwork ?? SystemIcons.Application,
		_ => _errorGlobal ?? SystemIcons.Application,
	};

	/// <summary>Same icon currently shown on the tray (for syncing the config window titlebar).</summary>
	public Icon TrayIconSnapshot => Resolve(State);

	public Icon ApplyState(AppTrayIconState state) {
		State = state;
		Icon icon = Resolve(state);
		_tray.Icon = icon;
		return icon;
	}
}
