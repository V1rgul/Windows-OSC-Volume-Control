using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using Image = System.Drawing.Image;
using BitmapSizeOptions = System.Windows.Media.Imaging.BitmapSizeOptions;

namespace WindowsOscVolumeControl;

public enum AppTrayIconState {
	STARTING_OR_INVALID_CONFIG,
	NETWORK_ERROR,
	OK,
}

public sealed class TrayController : IDisposable {
	const string DEFAULT_TEXT = "Windows OSC Volume Control";
	readonly NotifyIcon _trayIcon;
	readonly Icon _trayErrorGlobal;
	readonly Icon _trayErrorNetwork;
	readonly Icon _trayOk;

	public TrayController(Action onConfigure, Action onExit) {
		_trayErrorGlobal = loadTrayIconFile("error_global.ico");
		_trayErrorNetwork = loadTrayIconFile("error_network.ico");
		_trayOk = loadTrayIconFile("ok.ico");
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

	static Icon loadTrayIconFile(string fileName) {
		string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon", "app", fileName);
		try {
			return new Icon(path);
		} catch {
			return SystemIcons.Application;
		}
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
		AppTrayIconState.OK => _trayOk,
		AppTrayIconState.NETWORK_ERROR => _trayErrorNetwork,
		_ => _trayErrorGlobal,
	};

	public ImageSource windowIconSourceSnapshot {
		get {
			Icon icon = resolve(State);
			ImageSource source = Imaging.CreateBitmapSourceFromHIcon(
				icon.Handle,
				Int32Rect.Empty,
				BitmapSizeOptions.FromWidthAndHeight(32, 32));
			source.Freeze();
			return source;
		}
	}

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
