using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

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
		SurfaceBackground.Background = resolveOpaqueSurfaceBackground();
	}

	System.Windows.Media.Brush resolveOpaqueSurfaceBackground() {
		// 1) Prefer the actual Window style Background (Fluent theme).
		if (TryFindResource(typeof(Window)) is Style windowStyle) {
			foreach (SetterBase sb in windowStyle.Setters) {
				if (sb is not Setter s)
					continue;
				if (s.Property != System.Windows.Controls.Control.BackgroundProperty)
					continue;
				if (tryResolveBrushFromSetterValue(s.Value) is { } b1)
					return opacifyBrush(b1);
			}
		}

		// 2) Try common Fluent keys (varies by .NET/WPF version).
		object[] keys = [
			"WindowBackground",
			"WindowBackgroundBrush",
			"ApplicationBackground",
			"ApplicationBackgroundBrush",
			"SolidBackgroundFillColorBase",
			"SolidBackgroundFillColorBaseBrush",
			"SolidBackgroundFillColorBaseAlt",
			"SolidBackgroundFillColorBaseAltBrush",
			"LayerFillColorDefault",
			"LayerFillColorDefaultBrush",
			"LayerFillColorAlt",
			"LayerFillColorAltBrush",
		];

		foreach (object key in keys) {
			if (TryFindResource(key) is System.Windows.Media.Brush b2)
				return opacifyBrush(b2);
			if (TryFindResource(key) is System.Windows.Media.Color c)
				return new SolidColorBrush(System.Windows.Media.Color.FromArgb(255, c.R, c.G, c.B));
		}

		throw new InvalidOperationException(
			"Failed to resolve an opaque theme surface background brush for TrayMenuWindow. " +
			"Expected a Window Background setter or one of the common Fluent theme resource keys.");
	}

	System.Windows.Media.Brush? tryResolveBrushFromSetterValue(object? value) {
		if (value is System.Windows.Media.Brush b)
			return b;

		// Fluent theme uses DynamicResource in style setters.
		if (value is DynamicResourceExtension dre && TryFindResource(dre.ResourceKey) is System.Windows.Media.Brush db)
			return db;

		return null;
	}

	static System.Windows.Media.Brush opacifyBrush(System.Windows.Media.Brush brush) {
		if (brush is SolidColorBrush scb) {
			System.Windows.Media.Color c = scb.Color;
			if (c.A == 255)
				return brush;
			SolidColorBrush clone = new(System.Windows.Media.Color.FromArgb(255, c.R, c.G, c.B));
			clone.Freeze();
			return clone;
		}
		return brush;
	}

	public void setEndPointText(string text) {
		EndPointText.Text = string.IsNullOrWhiteSpace(text) ? "0.0.0.0:0" : text.Trim();
	}

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

