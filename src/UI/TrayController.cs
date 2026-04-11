using System.Drawing;
using System.Windows.Forms;
using System.Windows.Media;

namespace WindowsOscVolumeControl;

public enum AppTrayIconState {
	STARTING_OR_INVALID_CONFIG,
	NETWORK_ERROR,
	OK,
}

public sealed class TrayController : IDisposable {
	const string DEFAULT_TEXT = "Windows OSC Volume Control";
	readonly NotifyIcon _trayIcon;

	public TrayController(Action onConfigure, Action onExit) {
		_trayIcon = new NotifyIcon {
			ContextMenuStrip = new ContextMenuStrip(),
			Visible = true,
			Text = DEFAULT_TEXT,
		};
		ApplyState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);
		ContextMenuStrip menu = _trayIcon.ContextMenuStrip!;
		menu.Items.Add("Configure…", null, (_, _) => onConfigure());
		menu.Items.Add("Exit", null, (_, _) => onExit());
		_trayIcon.DoubleClick += (_, _) => onConfigure();
	}

	public void setStatusText(string? detail) {
		string text = string.IsNullOrWhiteSpace(detail)
			? DEFAULT_TEXT
			: DEFAULT_TEXT + " - " + detail.Trim();
		if (text.Length > 63)
			text = text[..63];
		_trayIcon.Text = text;
	}

	public AppTrayIconState State { get; private set; } = AppTrayIconState.STARTING_OR_INVALID_CONFIG;

	Icon resolve(AppTrayIconState state) => state switch {
		AppTrayIconState.OK => ResourceLoader.trayIconOk,
		AppTrayIconState.NETWORK_ERROR => ResourceLoader.trayIconErrorNetwork,
		_ => ResourceLoader.trayIconErrorGlobal,
	};

	public ImageSource windowIconSourceSnapshot => State switch {
		AppTrayIconState.OK => ResourceLoader.windowIconOk,
		AppTrayIconState.NETWORK_ERROR => ResourceLoader.windowIconErrorNetwork,
		_ => ResourceLoader.windowIconErrorGlobal,
	};

	public Icon ApplyState(AppTrayIconState state) {
		State = state;
		Icon icon = resolve(state);
		_trayIcon.Icon = icon;
		return icon;
	}

	public void Dispose() {
		_trayIcon.Visible = false;
		_trayIcon.ContextMenuStrip?.Dispose();
		_trayIcon.Dispose();
	}
}
