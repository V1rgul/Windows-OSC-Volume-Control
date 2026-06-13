using System.Runtime.InteropServices;
using System.Windows;
using WindowsOscVolumeControl.UI.Wpf.Theme;

namespace WindowsOscVolumeControl.UI.Tray;

public sealed partial class TrayMenuWindow : IDisposable {
	readonly Action _onConfigure;
	readonly Action _onExit;
	readonly Action _onClosed;
	bool _disposed;

	public TrayMenuWindow(Action onConfigure, Action onExit, Action onClosed) {
		_onConfigure = onConfigure ?? throw new ArgumentNullException(nameof(onConfigure));
		_onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));
		_onClosed = onClosed ?? throw new ArgumentNullException(nameof(onClosed));

		InitializeComponent();

		Loaded += (_, _) => applyWindowBackgroundFromThemeStyle();
		Deactivated += (_, _) => closeMenu();
		Closed += (_, _) => _onClosed();
	}

	void applyWindowBackgroundFromThemeStyle() {
		SurfaceBackground.Background = ThemeSurface.resolveOpaqueWindowSurfaceBrush(this);
	}

	public void setEndPointText(string text) {
		string raw = string.IsNullOrWhiteSpace(text) ? "0.0.0.0:0" : text.Trim();
		EndPointText.Text = formatEndPointDisplay(raw);
	}

	static string formatEndPointDisplay(string text) =>
		text.Replace(".", "\u200A.\u200A").Replace(":", "\u2009:\u2009");

	public void showAtCursor() {
		if (_disposed)
			return;

		System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
		POINT cursorPx = new(cursor.X, cursor.Y);
		MONITORINFO monitor = getMonitorInfo(cursorPx);
		uint dpi = getMonitorDpi(cursorPx);

		double scale = 96.0 / dpi;
		Rect workAreaDip = new(
			monitor.rcWork.left * scale,
			monitor.rcWork.top * scale,
			(monitor.rcWork.right - monitor.rcWork.left) * scale,
			(monitor.rcWork.bottom - monitor.rcWork.top) * scale);

		double cursorXDip = cursorPx.x * scale;
		double cursorYDip = cursorPx.y * scale;

		Show();
		UpdateLayout();

		double w = ActualWidth;
		double h = ActualHeight;

		double x = cursorXDip;
		double y = cursorYDip;

		if (x + w > workAreaDip.Right)
			x = Math.Max(workAreaDip.Left, workAreaDip.Right - w);
		if (y + h > workAreaDip.Bottom)
			y = Math.Max(workAreaDip.Top, cursorYDip - h);

		Left = x;
		Top = y;
		Activate();
	}

	void configure_Click(object sender, RoutedEventArgs e) {
		closeMenu();
		_onConfigure();
	}

	void exit_Click(object sender, RoutedEventArgs e) {
		closeMenu();
		_onExit();
	}

	void closeMenu() {
		if (_disposed)
			return;
		Hide();
		_onClosed();
	}

	public void Dispose() {
		if (_disposed)
			return;
		_disposed = true;
		try {
			Close();
		} catch {
			// ignored (shutdown path)
		}
	}

	static MONITORINFO getMonitorInfo(POINT point) {
		IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
		MONITORINFO info = new();
		info.cbSize = Marshal.SizeOf<MONITORINFO>();
		if (!GetMonitorInfo(monitor, ref info))
			throw new ExternalException("GetMonitorInfo failed.");
		return info;
	}

	static uint getMonitorDpi(POINT point) {
		IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
		if (GetDpiForMonitor(monitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
			return dpiX;
		return 96;
	}

	const uint MONITOR_DEFAULTTONEAREST = 2;

	[DllImport("user32.dll")]
	static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll", SetLastError = true)]
	static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

	[DllImport("shcore.dll")]
	static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

	enum MonitorDpiType {
		MDT_EFFECTIVE_DPI = 0,
	}

	[StructLayout(LayoutKind.Sequential)]
	struct POINT {
		public int x;
		public int y;
		public POINT(int x, int y) {
			this.x = x;
			this.y = y;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	struct RECT {
		public int left;
		public int top;
		public int right;
		public int bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MONITORINFO {
		public int cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}
}

