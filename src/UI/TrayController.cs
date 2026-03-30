using System.Drawing;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public enum AppTrayIconState {
	STARTING_OR_INVALID_CONFIG,
	NETWORK_ERROR,
	OK,
}

/// <summary>Tray <see cref="NotifyIcon"/>, context menu host, and status icons from <see cref="ResourceLoader"/>.</summary>
public sealed class TrayController {
	readonly NotifyIcon _trayIcon;
	readonly ResourceLoader _resources;

	public TrayController(ResourceLoader resources, Action onConfigure, Action onExit) {
		_resources = resources;
		_trayIcon = new NotifyIcon() {
			ContextMenuStrip = new ContextMenuStrip(),
			Visible = true,
			Text = "Windows OSC Volume Control"
		};
		ApplyState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);
		ContextMenuStrip menu = _trayIcon.ContextMenuStrip!;
		menu.Items.Add("Configure…", null, (_, _) => onConfigure());
		menu.Items.Add("Exit", null, (_, _) => onExit());
	}

	public void hide() => _trayIcon.Visible = false;

	public AppTrayIconState State { get; private set; } = AppTrayIconState.STARTING_OR_INVALID_CONFIG;

	Icon Resolve(AppTrayIconState state) => state switch {
		AppTrayIconState.OK => _resources.TrayIconOk,
		AppTrayIconState.NETWORK_ERROR => _resources.TrayIconErrorNetwork,
		_ => _resources.TrayIconErrorGlobal,
	};

	/// <summary>Same icon currently shown on the tray (for syncing the config window titlebar).</summary>
	public Icon TrayIconSnapshot => Resolve(State);

	public Icon ApplyState(AppTrayIconState state) {
		State = state;
		Icon icon = Resolve(state);
		_trayIcon.Icon = icon;
		return icon;
	}
}
