using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WindowsOscVolumeControl;

public partial class OSDController : Window {
	enum LayoutMode {
		NONE,
		BAR,
		ERROR_STATUS,
		TOGGLE_STATUS,
	}

	readonly record struct LayoutKey(LayoutMode mode, string labelText);
	readonly record struct PendingLevelUpdate(string rowLabel, float min, float max, float value, bool volumeIncreased, float step);

	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HWND_TOPMOST = new(-1);
	static readonly Brush ACTIVE_BRUSH = new SolidColorBrush(Color.FromRgb(122, 215, 122));
	static readonly Brush ERROR_BRUSH = new SolidColorBrush(Color.FromRgb(180, 180, 180));
	static readonly Brush OFF_BRUSH = new SolidColorBrush(Color.FromRgb(222, 87, 87));
	static readonly Brush TRACK_BRUSH = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

	public sealed class Config {
		public const int MIN_HEIGHT_PX = 48;
		public const int MAX_HEIGHT_PX = 600;
		public const uint MIN_DISPLAY_DURATION_MS = 200;
		public const uint MAX_DISPLAY_DURATION_MS = 60_000;

		public int HeightPx { get; set; } = 78;
		public uint DisplayDurationMs { get; set; } = 1000;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			HeightPx = other.HeightPx;
			DisplayDurationMs = other.DisplayDurationMs;
		}

		public static Config Clamped(Config? cfg) {
			cfg ??= new Config();
			uint duration = Math.Clamp(cfg.DisplayDurationMs, MIN_DISPLAY_DURATION_MS, MAX_DISPLAY_DURATION_MS);
			int height = Math.Clamp(cfg.HeightPx, MIN_HEIGHT_PX, MAX_HEIGHT_PX);
			return new Config {
				HeightPx = height,
				DisplayDurationMs = duration,
			};
		}
	}

	readonly DispatcherTimer _autoHideTimer;
	readonly DispatcherTimer _flashTimer;
	Config _config;
	double _minimumWidth;
	double _targetWidth;
	double _targetHeight;
	double _trackWidth;
	double _statusWidth;
	double _valueWidth;
	int _levelFracDigits = 2;
	float _cachedFaderStep = float.NaN;
	int _cachedFaderStepDigits = 2;
	LayoutKey _layoutKey = new(LayoutMode.NONE, "");
	bool _layoutMeasureDirty = true;
	bool _windowPlacementDirty = true;
	PendingLevelUpdate? _pendingLevelUpdate;
	bool _levelUpdateQueued;

	public OSDController(Config osdConfig) {
		InitializeComponent();
		_config = Config.Clamped(osdConfig);
		Opacity = 0.88;
		SizeToContent = SizeToContent.Manual;
		_autoHideTimer = new DispatcherTimer();
		_autoHideTimer.Tick += (_, _) => beginFade();
		_flashTimer = new DispatcherTimer {
			Interval = TimeSpan.FromMilliseconds(1000),
		};
		_flashTimer.Tick += (_, _) => {
			_flashTimer.Stop();
			FlashMarker.Visibility = Visibility.Collapsed;
		};

		ACTIVE_BRUSH.Freeze();
		ERROR_BRUSH.Freeze();
		OFF_BRUSH.Freeze();
		TRACK_BRUSH.Freeze();
		BarTrack.Background = TRACK_BRUSH;
		applySizing();
	}

	protected override void OnSourceInitialized(EventArgs e) {
		base.OnSourceInitialized(e);
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		int style = GetWindowLong(hwnd, GWL_EXSTYLE);
		SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
	}

	public void ApplyConfig(Config? cfg) {
		_config = Config.Clamped(cfg);
		_pendingLevelUpdate = null;
		applySizing();
	}

	void applySizing() {
		double scale = _config.HeightPx / 78.0;
		double horizontalPadding = Math.Max(12, 18 * scale);
		double screenInset = Math.Max(4, 8 * scale);
		double borderWidth = RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;
		_targetHeight = _config.HeightPx;
		_trackWidth = Math.Max(120, 194 * scale);
		_statusWidth = Math.Max(44, 56 * scale);
		_valueWidth = Math.Max(48, 56 * scale);
		_minimumWidth = Math.Ceiling(borderWidth + (2 * horizontalPadding) + _trackWidth + 10 + _valueWidth);
		_targetWidth = _minimumWidth;
		double maxWindowWidth = Math.Max(_minimumWidth, SystemParameters.WorkArea.Width - (2 * screenInset));
		double labelMaxWidth = Math.Max(80, maxWindowWidth - borderWidth - (2 * horizontalPadding) - _trackWidth - 10 - _valueWidth - 12);
		MinHeight = _targetHeight;
		MaxHeight = _targetHeight;
		MinWidth = _minimumWidth;
		MaxWidth = maxWindowWidth;
		Height = _targetHeight;
		Width = _targetWidth;
		RootBorder.CornerRadius = new CornerRadius(Math.Max(12, 14 * scale));
		RootBorder.Padding = new Thickness(horizontalPadding);
		LabelTextBlock.FontSize = Math.Max(12, 15 * scale);
		LabelTextBlock.MaxWidth = labelMaxWidth;
		StatusTextBlock.FontSize = Math.Max(13, 16 * scale);
		ValueTextBlock.FontSize = Math.Max(12, 15 * scale);
		ValueTextBlock.MinWidth = _valueWidth;
		ValueTextBlock.MaxWidth = _valueWidth;
		double flashMarkerSize = Math.Max(10, 12 * scale);
		double flashMarkerThickness = Math.Max(2, 3 * scale);
		FlashMarker.Width = flashMarkerSize;
		FlashMarker.Height = flashMarkerSize;
		FlashMarkerHorizontal.Width = flashMarkerSize;
		FlashMarkerHorizontal.Height = flashMarkerThickness;
		FlashMarkerHorizontal.CornerRadius = new CornerRadius(flashMarkerThickness / 2.0);
		FlashMarkerVertical.Width = flashMarkerThickness;
		FlashMarkerVertical.Height = flashMarkerSize;
		FlashMarkerVertical.CornerRadius = new CornerRadius(flashMarkerThickness / 2.0);
		double barHeight = Math.Max(16, 20 * scale);
		MiddleContent.MinWidth = _trackWidth;
		MiddleContent.MaxWidth = _trackWidth;
		MiddleContent.Width = _trackWidth;
		BarTrack.Height = barHeight;
		BarTrack.MinWidth = _trackWidth;
		BarTrack.MaxWidth = _trackWidth;
		BarTrack.Width = _trackWidth;
		BarFill.Height = barHeight;
		StatusTextBlock.MaxWidth = _trackWidth;
		double toggleIconSize = Math.Max(14, 18 * scale);
		ToggleIconContainer.Width = toggleIconSize;
		ToggleIconContainer.Height = toggleIconSize;
		ToggleSlash.X1 = toggleIconSize * 0.22;
		ToggleSlash.Y1 = toggleIconSize * 0.78;
		ToggleSlash.X2 = toggleIconSize * 0.78;
		ToggleSlash.Y2 = toggleIconSize * 0.22;
		ToggleSlash.StrokeThickness = Math.Max(2, toggleIconSize * 0.11);
		_layoutMeasureDirty = true;
		_windowPlacementDirty = true;
		reposition();
	}

	void beginFade() {
		_autoHideTimer.Stop();
		var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(220)) {
			FillBehavior = FillBehavior.Stop,
		};
		animation.Completed += (_, _) => {
			Opacity = 0.88;
			Hide();
		};
		BeginAnimation(OpacityProperty, animation);
	}

	void showNoActivate(bool layoutChanged) {
		BeginAnimation(OpacityProperty, null);
		bool wasVisible = IsVisible;
		bool needsPlacement = !wasVisible || layoutChanged || _layoutMeasureDirty || _windowPlacementDirty;
		IntPtr hwnd = IntPtr.Zero;
		if (needsPlacement) {
			hwnd = new WindowInteropHelper(this).EnsureHandle();
			if (layoutChanged || _layoutMeasureDirty) {
				double currentMinimumWidth = getCurrentMinimumWidth();
				MinWidth = currentMinimumWidth;
				double desiredWidth = measureWindowWidth();
				_targetWidth = desiredWidth;
				Width = desiredWidth;
				_layoutMeasureDirty = false;
			} else {
				Width = _targetWidth;
			}
			Height = _targetHeight;
		}
		if (!wasVisible) {
			Opacity = 0;
			Show();
		}
		if (needsPlacement) {
			placeWindow(hwnd, _targetWidth);
			_windowPlacementDirty = false;
		}
		_targetWidth = Math.Clamp(_targetWidth, MinWidth, MaxWidth);
		Width = _targetWidth;
		Height = _targetHeight;
		Opacity = 0.88;
		resetAutoHideTimer();
	}

	void resetAutoHideTimer() {
		_autoHideTimer.Stop();
		_autoHideTimer.Interval = TimeSpan.FromMilliseconds(_config.DisplayDurationMs);
		_autoHideTimer.Start();
	}

	void cancelPendingLevelUpdate() => _pendingLevelUpdate = null;

	void queueLevelUpdate(PendingLevelUpdate update) {
		_pendingLevelUpdate = update;
		if (_levelUpdateQueued)
			return;
		_levelUpdateQueued = true;
		_ = Dispatcher.BeginInvoke(flushPendingLevelUpdate, DispatcherPriority.Background);
	}

	void flushPendingLevelUpdate() {
		PendingLevelUpdate? pending = _pendingLevelUpdate;
		_pendingLevelUpdate = null;
		_levelUpdateQueued = false;
		if (pending == null)
			return;
		showLevelNow(pending.Value.rowLabel, pending.Value.min, pending.Value.max, pending.Value.value, pending.Value.volumeIncreased, pending.Value.step);
	}

	void placeWindow(IntPtr hwnd, double width) {
		var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
		double insetDip = Math.Max(4, _targetHeight * 0.08);
		int widthPx = (int)Math.Round(width * dpi.DpiScaleX);
		int heightPx = (int)Math.Round(_targetHeight * dpi.DpiScaleY);
		int insetXPx = (int)Math.Round(insetDip * dpi.DpiScaleX);
		int insetYPx = (int)Math.Round(insetDip * dpi.DpiScaleY);
		RECT workArea = getMonitorWorkArea(hwnd);
		int xPx = workArea.right - widthPx - insetXPx;
		int yPx = workArea.bottom - heightPx - insetYPx;
		Left = xPx / dpi.DpiScaleX;
		Top = yPx / dpi.DpiScaleY;
		SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
	}

	double measureWindowWidth() {
		RootBorder.Measure(new System.Windows.Size(double.PositiveInfinity, _targetHeight));
		double currentMinimumWidth = getCurrentMinimumWidth();
		return Math.Clamp(Math.Ceiling(RootBorder.DesiredSize.Width), currentMinimumWidth, MaxWidth);
	}

	bool updateLayoutKey(LayoutMode mode, string rowLabel) {
		string label = normalizeLabel(rowLabel);
		var next = new LayoutKey(mode, label);
		if (next == _layoutKey)
			return false;
		_layoutKey = next;
		_layoutMeasureDirty = true;
		_windowPlacementDirty = true;
		return true;
	}

	double getCurrentMinimumWidth() => BarTrack.Visibility == Visibility.Visible
		? _minimumWidth
		: Math.Ceiling(RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right + RootBorder.Padding.Left + RootBorder.Padding.Right + 24);

	void reposition() => reposition(_targetWidth);

	void reposition(double width) {
		Rect workArea = SystemParameters.WorkArea;
		double inset = Math.Max(4, _targetHeight * 0.08);
		Left = workArea.Right - width - inset;
		Top = workArea.Bottom - _targetHeight - inset;
	}

	void hideExtraVisuals() {
		LabelTextBlock.Visibility = Visibility.Collapsed;
		StatusTextBlock.Visibility = Visibility.Collapsed;
		FlashMarker.Visibility = Visibility.Collapsed;
		FlashMarkerVertical.Visibility = Visibility.Collapsed;
		FlashTransform.X = 0;
		FlashTransform.Y = 0;
		ToggleIconContainer.Visibility = Visibility.Collapsed;
		ToggleSlash.Visibility = Visibility.Collapsed;
		ValueTextBlock.Visibility = Visibility.Visible;
		BarTrack.Visibility = Visibility.Visible;
		configureMiddleContentForBar();
	}

	void configureMiddleContentForBar() {
		MiddleContent.MinWidth = _trackWidth;
		MiddleContent.MaxWidth = _trackWidth;
		MiddleContent.Width = _trackWidth;
	}

	void configureMiddleContentForStatus() {
		MiddleContent.MinWidth = _statusWidth;
		MiddleContent.MaxWidth = _statusWidth;
		MiddleContent.Width = _statusWidth;
	}

	static string normalizeLabel(string rowLabel) => string.IsNullOrWhiteSpace(rowLabel)
		? ""
		: rowLabel.Trim();

	void applyLabel(string rowLabel) {
		string label = normalizeLabel(rowLabel);
		if (label.Length == 0) {
			LabelTextBlock.Text = "";
			LabelTextBlock.Visibility = Visibility.Collapsed;
			return;
		}
		LabelTextBlock.Text = label;
		LabelTextBlock.Visibility = Visibility.Visible;
	}

	int getOsdFractionalDigits(float step) {
		if (!float.IsFinite(step) || step <= 0f)
			return Math.Max(0, _cachedFaderStepDigits);
		if (!float.IsFinite(_cachedFaderStep) || Math.Abs(_cachedFaderStep - step) > 1e-6f) {
			_cachedFaderStep = step;
			_cachedFaderStepDigits = FaderFloatUtil.GetOsdFractionalDigitsFromStep(step);
		}
		return _cachedFaderStepDigits;
	}

	public void ShowPending() => ShowPending("", float.NaN);

	public void ShowPending(string rowLabel) => ShowPending(rowLabel, float.NaN);

	public void ShowPending(string rowLabel, float faderStepForLayout) {
		cancelPendingLevelUpdate();
		_levelFracDigits = getOsdFractionalDigits(faderStepForLayout);
		bool layoutChanged = updateLayoutKey(LayoutMode.BAR, rowLabel);
		hideExtraVisuals();
		applyLabel(rowLabel);
		BarFill.Width = 0;
		BarFill.Background = TRACK_BRUSH;
		BarFill.CornerRadius = new CornerRadius(BarTrack.Height / 2.0, 0, 0, BarTrack.Height / 2.0);
		ValueTextBlock.Text = "Pending";
		showNoActivate(layoutChanged);
	}

	public void ShowError() {
		cancelPendingLevelUpdate();
		bool layoutChanged = updateLayoutKey(LayoutMode.ERROR_STATUS, "");
		hideExtraVisuals();
		configureMiddleContentForStatus();
		LabelTextBlock.Visibility = Visibility.Collapsed;
		LabelTextBlock.Text = "";
		BarTrack.Visibility = Visibility.Collapsed;
		StatusTextBlock.Visibility = Visibility.Visible;
		StatusTextBlock.Text = "Error";
		StatusTextBlock.Foreground = ERROR_BRUSH;
		ValueTextBlock.Visibility = Visibility.Collapsed;
		showNoActivate(layoutChanged);
	}

	public void ShowLevel(string rowLabel, float min, float max, float value, bool volumeIncreased, float step) {
		queueLevelUpdate(new PendingLevelUpdate(rowLabel, min, max, value, volumeIncreased, step));
	}

	void showLevelNow(string rowLabel, float min, float max, float value, bool volumeIncreased, float step) {
		_levelFracDigits = getOsdFractionalDigits(step);
		bool layoutChanged = updateLayoutKey(LayoutMode.BAR, rowLabel);
		hideExtraVisuals();
		applyLabel(rowLabel);
		BarFill.Background = ACTIVE_BRUSH;
		double normalized = normalized01(value, min, max);
		updateBarFill(normalized);
		ValueTextBlock.Text = FaderFloatUtil.FormatOsdLevelValue(value, _levelFracDigits);
		FlashMarkerVertical.Visibility = volumeIncreased ? Visibility.Visible : Visibility.Collapsed;
		positionFlash(volumeIncreased);
		FlashMarker.Visibility = Visibility.Visible;
		_flashTimer.Stop();
		_flashTimer.Start();
		showNoActivate(layoutChanged);
	}

	public void ShowToggle(string displayName, bool enabled) {
		cancelPendingLevelUpdate();
		bool layoutChanged = updateLayoutKey(LayoutMode.TOGGLE_STATUS, displayName);
		hideExtraVisuals();
		configureMiddleContentForStatus();
		applyLabel(string.IsNullOrWhiteSpace(displayName) ? "Toggle" : displayName);
		BarTrack.Visibility = Visibility.Collapsed;
		ValueTextBlock.Visibility = Visibility.Collapsed;
		StatusTextBlock.Visibility = Visibility.Visible;
		StatusTextBlock.Text = enabled ? "ON" : "OFF";
		StatusTextBlock.Foreground = enabled ? ACTIVE_BRUSH : OFF_BRUSH;
		ToggleIconContainer.Visibility = Visibility.Visible;
		if (enabled) {
			ToggleEllipse.Fill = ACTIVE_BRUSH;
			ToggleEllipse.Stroke = ACTIVE_BRUSH;
			ToggleSlash.Visibility = Visibility.Collapsed;
		} else {
			ToggleEllipse.Fill = Brushes.Transparent;
			ToggleEllipse.Stroke = OFF_BRUSH;
			ToggleSlash.Stroke = OFF_BRUSH;
			ToggleSlash.Visibility = Visibility.Visible;
		}
		showNoActivate(layoutChanged);
	}

	void updateBarFill(double normalized) {
		BarTrack.Width = _trackWidth;
		double fillWidth = Math.Max(0, _trackWidth * Math.Clamp(normalized, 0.0, 1.0));
		BarFill.Width = fillWidth;
		double radius = BarTrack.Height / 2.0;
		BarFill.CornerRadius = fillWidth >= _trackWidth - 1
			? new CornerRadius(radius)
			: new CornerRadius(radius, 0, 0, radius);
	}

	void positionFlash(bool volumeIncreased) {
		double fillEndX = BarFill.Width;
		double outsideGap = Math.Max(4, BarTrack.Height * 0.18);
		double markerWidth = FlashMarker.Width;
		double insideGap = Math.Max(6, markerWidth * 0.35);
		double verticalOffset = 0;
		double x = volumeIncreased
			? Math.Min(Math.Max(0, fillEndX + outsideGap), Math.Max(0, _trackWidth - markerWidth))
			: Math.Min(Math.Max(0, fillEndX - markerWidth - insideGap), Math.Max(0, _trackWidth - markerWidth));
		FlashTransform.X = x;
		FlashTransform.Y = verticalOffset;
	}

	static double normalized01(float value, float min, float max) {
		if (max - min < 1e-9f)
			return 0.5;
		double t = (value - min) / (max - min);
		return Math.Clamp(t, 0.0, 1.0);
	}

	const int GWL_EXSTYLE = -20;
	const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

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

	static RECT getMonitorWorkArea(IntPtr hwnd) {
		IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
		var monitorInfo = new MONITORINFO {
			cbSize = Marshal.SizeOf<MONITORINFO>(),
		};
		if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
			return new RECT { left = 0, top = 0, right = 0, bottom = 0 };
		return monitorInfo.rcWork;
	}

	[LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

	[LibraryImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[LibraryImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

	[LibraryImport("user32.dll")]
	private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

	[LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
}
