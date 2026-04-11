using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using BitmapSizeOptions = System.Windows.Media.Imaging.BitmapSizeOptions;

namespace WindowsOscVolumeControl;

public static class ResourceLoader {
	static readonly Lazy<Icon> _trayIconErrorGlobal = new(() => loadTrayIcon("error_global.ico"));
	public static Icon trayIconErrorGlobal => _trayIconErrorGlobal.Value;

	static readonly Lazy<Icon> _trayIconErrorNetwork = new(() => loadTrayIcon("error_network.ico"));
	public static Icon trayIconErrorNetwork => _trayIconErrorNetwork.Value;

	static readonly Lazy<Icon> _trayIconOk = new(() => loadTrayIcon("ok.ico"));
	public static Icon trayIconOk => _trayIconOk.Value;

	static readonly Lazy<BitmapSource> _windowIconErrorGlobal =
		new(() => frozenWindowBitmapFromIcon(_trayIconErrorGlobal.Value));
	public static BitmapSource windowIconErrorGlobal => _windowIconErrorGlobal.Value;

	static readonly Lazy<BitmapSource> _windowIconErrorNetwork =
		new(() => frozenWindowBitmapFromIcon(_trayIconErrorNetwork.Value));
	public static BitmapSource windowIconErrorNetwork => _windowIconErrorNetwork.Value;

	static readonly Lazy<BitmapSource> _windowIconOk =
		new(() => frozenWindowBitmapFromIcon(_trayIconOk.Value));
	public static BitmapSource windowIconOk => _windowIconOk.Value;

	static Icon loadTrayIcon(string fileName) {
		string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon", "app", fileName);
		try {
			return new Icon(path);
		} catch {
			return SystemIcons.Application;
		}
	}

	static BitmapSource frozenWindowBitmapFromIcon(Icon icon) {
		BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
			icon.Handle,
			Int32Rect.Empty,
			BitmapSizeOptions.FromWidthAndHeight(32, 32));
		source.Freeze();
		return source;
	}
}
