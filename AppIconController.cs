using System.Drawing;
using System.Windows.Forms;

namespace X32VolumeHijacker;

public enum AppTrayIconState {
	StartingOrInvalidConfig,
	NetworkError,
	Ok,
}

/// <summary>Applies status icons from <see cref="ResourceLoader"/> to the tray (and optionally the config form titlebar).</summary>
public sealed class AppIconController {
	readonly NotifyIcon _tray;
	readonly ResourceLoader _resources;

	public AppIconController(NotifyIcon tray, ResourceLoader resources) {
		_tray = tray;
		_resources = resources;
	}

	public AppTrayIconState State { get; private set; } = AppTrayIconState.StartingOrInvalidConfig;

	Icon Resolve(AppTrayIconState state) => state switch {
		AppTrayIconState.Ok => _resources.TrayIconOk,
		AppTrayIconState.NetworkError => _resources.TrayIconErrorNetwork,
		_ => _resources.TrayIconErrorGlobal,
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
