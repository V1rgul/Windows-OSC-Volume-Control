using System.Drawing;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public enum AppTrayIconState {
	STARTING_OR_INVALID_CONFIG,
	NETWORK_ERROR,
	OK,
}

/// <summary>Tray <see cref="NotifyIcon"/>, context menu host, and status icons from <see cref="ResourceLoader"/>.</summary>
public sealed class TrayController : IDisposable {
	const string DEFAULT_TEXT = "Windows OSC Volume Control";
	readonly NotifyIcon _trayIcon;
	readonly ResourceLoader _resources;

	public TrayController(ResourceLoader resources, Action onConfigure, Action onExit) {
		_resources = resources;
		_trayIcon = new NotifyIcon() {
			ContextMenuStrip = new ContextMenuStrip(),
			Visible = true,
			Text = DEFAULT_TEXT
		};
		ApplyState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);
		ContextMenuStrip menu = _trayIcon.ContextMenuStrip!;
		menu.Items.Add("Configure…", null, (_, _) => onConfigure());
		menu.Items.Add("Exit", null, (_, _) => onExit());
	}

	public void hide() => _trayIcon.Visible = false;

	public void setStatusText(string? detail) {
		string text = string.IsNullOrWhiteSpace(detail)
			? DEFAULT_TEXT
			: DEFAULT_TEXT + " - " + detail.Trim();
		if (text.Length > 63)
			text = text[..63];
		_trayIcon.Text = text;
	}

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

	public void Dispose() {
		_trayIcon.Visible = false;
		_trayIcon.ContextMenuStrip?.Dispose();
		_trayIcon.Dispose();
	}
}
