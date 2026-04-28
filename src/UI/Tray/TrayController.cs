using System.Net;
using System.Windows.Media;

namespace WindowsOscVolumeControl.UI.Tray;

public enum AppTrayIconState {
	STARTING_OR_INVALID_CONFIG,
	NETWORK_ERROR,
	OK,
}

public sealed class TrayController : IDisposable {
	const string DEFAULT_TEXT = "Windows OSC Volume Control";
	readonly NotifyIcon _trayIcon;
	readonly TrayMenuWindow _menuWindow;
	volatile bool _menuOpen;
	string _endPointText = "0.0.0.0:0";

	public TrayController(Action onConfigure, Action onExit) {
		_menuWindow = new TrayMenuWindow(
			onConfigure: () => onConfigure(),
			onExit: () => onExit(),
			onClosed: () => _menuOpen = false);

		_trayIcon = new NotifyIcon { Visible = true, Text = DEFAULT_TEXT };
		ApplyState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);

		_trayIcon.MouseUp += (_, e) => {
			if (e.Button != MouseButtons.Right)
				return;
			showTrayMenu();
		};

		_trayIcon.DoubleClick += (_, _) => onConfigure();
		_trayIcon.MouseDoubleClick += (_, e) => {
			if (e.Button == MouseButtons.Left)
				onConfigure();
		};
	}

	public void setOscEndPoint(IPEndPoint endPoint) {
		ArgumentNullException.ThrowIfNull(endPoint);
		_endPointText = $"{endPoint.Address}:{endPoint.Port}";
		_menuWindow.setEndPointText(_endPointText);
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

	void showTrayMenu() {
		if (_menuOpen)
			return;

		_menuOpen = true;
		_menuWindow.setEndPointText(_endPointText);
		_menuWindow.showAtCursor();
	}

	public void Dispose() {
		_trayIcon.Visible = false;
		_trayIcon.Dispose();
		_menuWindow.Dispose();
	}
}
