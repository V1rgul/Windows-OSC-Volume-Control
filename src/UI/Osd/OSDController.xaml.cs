using System.Globalization;
using System.Runtime.InteropServices;
using Result;
using WindowsOscVolumeControl.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WindowsOscVolumeControl.UI.Osd;

public partial class OSDController {
	enum LayoutMode {
		NONE,
		BAR,
		ERROR_STATUS,
		TOGGLE_STATUS,
	}

	readonly record struct LayoutKey(LayoutMode mode, string labelText);
	readonly record struct PendingLevelUpdate(string rowLabel, double normalizedRatio, string displayText, bool volumeIncreased);

	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HWND_TOPMOST = new(-1);
	static readonly Brush ACTIVE_BRUSH = new SolidColorBrush(Color.FromRgb(122, 215, 122));
	static readonly Brush ERROR_BRUSH = new SolidColorBrush(Color.FromRgb(180, 180, 180));
	static readonly Brush OFF_BRUSH = new SolidColorBrush(Color.FromRgb(222, 87, 87));
	static readonly Brush TRACK_BRUSH = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255));

	/// <summary>Bar row stem cap (dip per window height); same as toggle icon diameter for row alignment.</summary>
	const double RATIO_STEM_HEIGHT = 0.34;
	/// <summary>Track region width relative to window height.</summary>
	const double RATIO_TRACK_WIDTH = 3.0;
	/// <summary>Value column width as a fraction of track width (dimensionless).</summary>
	const double RATIO_VALUE_COLUMN_WIDTH_PER_TRACK_WIDTH = 0.29;
	const double RATIO_VALUE_COLUMN_WIDTH = RATIO_TRACK_WIDTH * RATIO_VALUE_COLUMN_WIDTH_PER_TRACK_WIDTH;
	/// <summary>Gap track→value column and flash vertical leg (scaled with window height).</summary>
	const double RATIO_CHROME_GAP = RATIO_STEM_HEIGHT * 0.54;
	const double RATIO_LABEL_RIGHT_MARGIN = RATIO_STEM_HEIGHT * 0.63;
	/// <summary>Middle column right margin and flash inside gap (scaled with window height).</summary>
	const double RATIO_MIDDLE_COLUMN_RIGHT_MARGIN = RATIO_STEM_HEIGHT * 0.33;
	/// <summary>Screen inset from work area and toggle stack left margin (scaled with window height).</summary>
	const double RATIO_EDGE_MARGIN = RATIO_STEM_HEIGHT * 0.42;
	const double RATIO_CORNER_RADIUS = RATIO_STEM_HEIGHT * 0.79;
	const double RATIO_FLASH_MARKER_SIZE = RATIO_STEM_HEIGHT * 0.67;
	const double RATIO_FLASH_VERTICAL_BAR_THICKNESS = RATIO_STEM_HEIGHT * 0.17;
	const double RATIO_TOGGLE_ICON_STROKE_WIDTH = RATIO_STEM_HEIGHT * 0.13;
	/// <summary>Extra minimum width when the progress bar row is hidden (scaled with window height).</summary>
	const double RATIO_STATUS_ROW_MINIMUM_WIDTH_INCREMENT = RATIO_STEM_HEIGHT * 1.29;
	const double RATIO_FLASH_VERTICAL_LINE_TOP_MARGIN = RATIO_STEM_HEIGHT * 0.04;
	const double RATIO_FLASH_MARKER_OUTSIDE_GAP = RATIO_STEM_HEIGHT * 0.21;
	const double RATIO_FONT_SIZE_SEARCH_LOWER_BOUND = 0.1;
	const double RATIO_FONT_SIZE_SEARCH_UPPER_BOUND = 0.38;
	const double RATIO_FONT_SIZE_FINE_TUNING_UPPER_CAP = 0.4;
	const double RATIO_FONT_SIZE_FINE_TUNING_STEP = 0.15;

	public sealed class Config {
		public enum OsdScreenAnchor {
			TOP_LEFT,
			TOP_CENTER,
			TOP_RIGHT,
			MIDDLE_LEFT,
			MIDDLE_RIGHT,
			BOTTOM_LEFT,
			BOTTOM_CENTER,
			BOTTOM_RIGHT,
		}

		public const int MIN_HEIGHT_DIP = 48;
		public const int MAX_HEIGHT_DIP = 600;
		public const uint MIN_DISPLAY_DURATION_MS = 200;
		public const uint MAX_DISPLAY_DURATION_MS = 60_000;

		public int heightDip { get; set; } = 80;
		public uint DisplayDurationMs { get; set; } = 1000;
		public OsdScreenAnchor screenAnchor { get; set; } = OsdScreenAnchor.BOTTOM_CENTER;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			heightDip = other.heightDip;
			DisplayDurationMs = other.DisplayDurationMs;
			screenAnchor = other.screenAnchor;
		}

		public static OsdScreenAnchor clampScreenAnchor(OsdScreenAnchor anchor) =>
			Enum.IsDefined(anchor) ? anchor : OsdScreenAnchor.BOTTOM_RIGHT;

		public static Config Clamped(Config? cfg) {
			cfg ??= new Config();
			uint duration = Math.Clamp(cfg.DisplayDurationMs, MIN_DISPLAY_DURATION_MS, MAX_DISPLAY_DURATION_MS);
			int height = Math.Clamp(cfg.heightDip, MIN_HEIGHT_DIP, MAX_HEIGHT_DIP);
			return new Config {
				heightDip = height,
				DisplayDurationMs = duration,
				screenAnchor = clampScreenAnchor(cfg.screenAnchor),
			};
		}

		public static Result<int> parseHeightDip(string? text) {
			if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
				return new ResultError.Generic.Parsing { message = "Must be an integer." };
			if (parsed < MIN_HEIGHT_DIP || parsed > MAX_HEIGHT_DIP)
				return new ResultError.Generic.Parsing { message = $"Must be between {MIN_HEIGHT_DIP} and {MAX_HEIGHT_DIP}." };
			return parsed;
		}

		public static Result<uint> parseDisplayDurationMs(string? text) {
			if (!uint.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
				return new ResultError.Generic.Parsing { message = "Must be an integer." };
			if (parsed < MIN_DISPLAY_DURATION_MS || parsed > MAX_DISPLAY_DURATION_MS)
				return new ResultError.Generic.Parsing { message = $"Must be between {MIN_DISPLAY_DURATION_MS} and {MAX_DISPLAY_DURATION_MS}." };
			return parsed;
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
	double _barHeight;
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
			Interval = TimeSpan.FromMilliseconds(200),
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
		_ = SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
	}

	public void ApplyConfig(Config? cfg) {
		_config = Config.Clamped(cfg);
		_pendingLevelUpdate = null;
		applySizing();
	}

	void applySizing() {
		double H = _config.heightDip;
		double borderWidth = RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right;
		double borderTB = RootBorder.BorderThickness.Top + RootBorder.BorderThickness.Bottom;
		double screenInset = H * RATIO_EDGE_MARGIN;
		_targetHeight = H;
		_trackWidth = H * RATIO_TRACK_WIDTH;
		_statusWidth = H * RATIO_VALUE_COLUMN_WIDTH;
		_valueWidth = H * RATIO_VALUE_COLUMN_WIDTH;
		double gapTrackToValue = H * RATIO_CHROME_GAP;
		double stemH = H * RATIO_STEM_HEIGHT;
		var barRowTypeface = new Typeface(LabelTextBlock.FontFamily, LabelTextBlock.FontStyle, LabelTextBlock.FontWeight, LabelTextBlock.FontStretch);
		double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		const string stemProbe = "Wy";
		FormattedText probeLine(double sz) => new(stemProbe, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, barRowTypeface, sz, Brushes.White, pixelsPerDip);
		double loFont = H * RATIO_FONT_SIZE_SEARCH_LOWER_BOUND;
		double hiFont = H * RATIO_FONT_SIZE_SEARCH_UPPER_BOUND;
		if (Math.Ceiling(probeLine(hiFont).Height) <= stemH) {
			loFont = hiFont;
		} else {
			for (int i = 0; i < 26; i++) {
				double mid = (loFont + hiFont) * 0.5;
				if (Math.Ceiling(probeLine(mid).Height) <= stemH)
					loFont = mid;
				else
					hiFont = mid;
			}
		}
		double fontSizeDip = loFont;
		while (fontSizeDip + RATIO_FONT_SIZE_FINE_TUNING_STEP <= H * RATIO_FONT_SIZE_FINE_TUNING_UPPER_CAP && Math.Ceiling(probeLine(fontSizeDip + RATIO_FONT_SIZE_FINE_TUNING_STEP).Height) <= stemH)
			fontSizeDip += RATIO_FONT_SIZE_FINE_TUNING_STEP;
		_barHeight = Math.Min(stemH, Math.Ceiling(probeLine(fontSizeDip).Height));
		double toggleIconSize = H * RATIO_STEM_HEIGHT;
		double rowHeight = Math.Max(_barHeight, toggleIconSize);
		double padDip = (H - borderTB - rowHeight) / 2;
		if (padDip < 0)
			padDip = 0;
		_minimumWidth = Math.Ceiling(borderWidth + (2 * padDip) + _trackWidth + gapTrackToValue + _valueWidth);
		_targetWidth = _minimumWidth;
		double maxWindowWidth = Math.Max(_minimumWidth, SystemParameters.WorkArea.Width - (2 * screenInset));
		double labelChrome = borderWidth + (2 * padDip) + _trackWidth + gapTrackToValue + _valueWidth + H * RATIO_LABEL_RIGHT_MARGIN;
		double labelMaxWidth = Math.Max(0, maxWindowWidth - labelChrome);
		MinHeight = _targetHeight;
		MaxHeight = _targetHeight;
		MinWidth = _minimumWidth;
		MaxWidth = maxWindowWidth;
		Height = _targetHeight;
		Width = _targetWidth;
		RootBorder.CornerRadius = new CornerRadius(H * RATIO_CORNER_RADIUS);
		RootBorder.Padding = new Thickness(padDip);
		LabelTextBlock.FontSize = fontSizeDip;
		LabelTextBlock.LineHeight = _barHeight;
		LabelTextBlock.MinHeight = rowHeight;
		LabelTextBlock.MaxHeight = rowHeight;
		LabelTextBlock.Margin = new Thickness(0, 0, H * RATIO_LABEL_RIGHT_MARGIN, 0);
		LabelTextBlock.MaxWidth = labelMaxWidth;
		ValueTextBlock.FontSize = fontSizeDip;
		ValueTextBlock.LineHeight = _barHeight;
		ValueTextBlock.MinHeight = rowHeight;
		ValueTextBlock.MaxHeight = rowHeight;
		StatusTextBlock.FontSize = fontSizeDip;
		StatusTextBlock.LineHeight = _barHeight;
		StatusTextBlock.MinHeight = rowHeight;
		StatusTextBlock.MaxHeight = rowHeight;
		MiddleContent.Margin = new Thickness(0, 0, H * RATIO_MIDDLE_COLUMN_RIGHT_MARGIN, 0);
		ValueTextBlock.MinWidth = _valueWidth;
		ValueTextBlock.MaxWidth = _valueWidth;
		double flashMarkerSize = H * RATIO_FLASH_MARKER_SIZE;
		double flashMarkerThickness = H * RATIO_FLASH_VERTICAL_BAR_THICKNESS;
		FlashMarker.Width = flashMarkerSize;
		FlashMarker.Height = flashMarkerSize;
		FlashMarkerHorizontal.Width = flashMarkerSize;
		FlashMarkerHorizontal.Height = flashMarkerThickness;
		FlashMarkerHorizontal.CornerRadius = new CornerRadius(flashMarkerThickness / 2.0);
		FlashMarkerVertical.Width = flashMarkerThickness;
		FlashMarkerVertical.Height = H * RATIO_CHROME_GAP;
		FlashMarkerVertical.Margin = new Thickness(0, H * RATIO_FLASH_VERTICAL_LINE_TOP_MARGIN, 0, 0);
		FlashMarkerVertical.CornerRadius = new CornerRadius(flashMarkerThickness / 2.0);
		MiddleContent.MinWidth = _trackWidth;
		MiddleContent.MaxWidth = _trackWidth;
		MiddleContent.Width = _trackWidth;
		MiddleContent.MinHeight = rowHeight;
		MiddleContent.MaxHeight = rowHeight;
		RightContent.MinHeight = rowHeight;
		RightContent.MaxHeight = rowHeight;
		BarTrack.Height = _barHeight;
		BarTrack.CornerRadius = new CornerRadius(_barHeight / 2.0);
		BarTrack.MinWidth = _trackWidth;
		BarTrack.MaxWidth = _trackWidth;
		BarTrack.Width = _trackWidth;
		BarFill.Height = _barHeight;
		StatusTextBlock.MaxWidth = _trackWidth;
		ToggleIconContainer.Width = toggleIconSize;
		ToggleIconContainer.Height = toggleIconSize;
		ToggleIconContainer.Margin = new Thickness(H * RATIO_EDGE_MARGIN, 0, 0, 0);
		ToggleSlash.X1 = toggleIconSize * 0.22;
		ToggleSlash.Y1 = toggleIconSize * 0.78;
		ToggleSlash.X2 = toggleIconSize * 0.78;
		ToggleSlash.Y2 = toggleIconSize * 0.22;
		double strokeDip = H * RATIO_TOGGLE_ICON_STROKE_WIDTH;
		ToggleSlash.StrokeThickness = strokeDip;
		ToggleEllipse.StrokeThickness = strokeDip;
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
				MinWidth = getCurrentMinimumWidth();
				double desiredWidth = measureWindowWidth();
				_targetWidth = Math.Clamp(desiredWidth, MinWidth, MaxWidth);
				_layoutMeasureDirty = false;
			}
			Height = _targetHeight;
			Width = _targetWidth;
		}
		if (!wasVisible) {
			Opacity = 0;
			Show();
		}
		if (needsPlacement) {
			placeWindow(hwnd, _targetWidth);
			_windowPlacementDirty = false;
		}
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
		showLevelNow(pending.Value.rowLabel, pending.Value.normalizedRatio, pending.Value.displayText, pending.Value.volumeIncreased);
	}

	void placeWindow(IntPtr hwnd, double width) =>
		applyOverlayPlacement(width, hwnd, callSetWindowPos: true);

	/// <summary>Pixel work-area placement, then <see cref="Window.Left"/>/<see cref="Window.Top"/> from those pixels; optionally calls Win32 <c>SetWindowPos</c>.</summary>
	void applyOverlayPlacement(double widthDip, IntPtr hwnd, bool callSetWindowPos) {
		DpiScale dpi = VisualTreeHelper.GetDpi(this);
		double insetDip = _targetHeight * RATIO_EDGE_MARGIN;
		int widthPx = (int)Math.Round(widthDip * dpi.DpiScaleX);
		int heightPx = (int)Math.Round(_targetHeight * dpi.DpiScaleY);
		int insetXPx = (int)Math.Round(insetDip * dpi.DpiScaleX);
		int insetYPx = (int)Math.Round(insetDip * dpi.DpiScaleY);
		RECT workArea = hwnd != IntPtr.Zero
			? getMonitorWorkArea(hwnd)
			: dipWorkAreaToPixelRect(SystemParameters.WorkArea, dpi);
		computePlacementPixels(workArea, widthPx, heightPx, insetXPx, insetYPx, _config.screenAnchor, out int xPx, out int yPx);
		Left = xPx / dpi.DpiScaleX;
		Top = yPx / dpi.DpiScaleY;
		if (callSetWindowPos)
			_ = SetWindowPos(hwnd, HWND_TOPMOST, xPx, yPx, widthPx, heightPx, SWP_NOACTIVATE | SWP_SHOWWINDOW);
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
		: Math.Ceiling(RootBorder.BorderThickness.Left + RootBorder.BorderThickness.Right + RootBorder.Padding.Left + RootBorder.Padding.Right + _targetHeight * RATIO_STATUS_ROW_MINIMUM_WIDTH_INCREMENT);

	void reposition() => reposition(_targetWidth);

	void reposition(double width) {
		IntPtr hwnd = new WindowInteropHelper(this).Handle;
		applyOverlayPlacement(width, hwnd, callSetWindowPos: false);
	}

	static RECT dipWorkAreaToPixelRect(Rect wa, DpiScale dpi) => new() {
		left = (int)Math.Round(wa.Left * dpi.DpiScaleX),
		top = (int)Math.Round(wa.Top * dpi.DpiScaleY),
		right = (int)Math.Round(wa.Right * dpi.DpiScaleX),
		bottom = (int)Math.Round(wa.Bottom * dpi.DpiScaleY),
	};

	static void computePlacementPixels(RECT work, int widthPx, int heightPx, int insetXPx, int insetYPx, Config.OsdScreenAnchor anchor, out int xPx, out int yPx) {
		int wl = work.left;
		int wt = work.top;
		int ww = work.right - wl;
		int wh = work.bottom - wt;
		switch (anchor) {
			case Config.OsdScreenAnchor.TOP_LEFT:
				xPx = wl + insetXPx;
				yPx = wt + insetYPx;
				break;
			case Config.OsdScreenAnchor.TOP_CENTER:
				xPx = wl + (ww - widthPx) / 2;
				yPx = wt + insetYPx;
				break;
			case Config.OsdScreenAnchor.TOP_RIGHT:
				xPx = work.right - widthPx - insetXPx;
				yPx = wt + insetYPx;
				break;
			case Config.OsdScreenAnchor.MIDDLE_LEFT:
				xPx = wl + insetXPx;
				yPx = wt + (wh - heightPx) / 2;
				break;
			case Config.OsdScreenAnchor.MIDDLE_RIGHT:
				xPx = work.right - widthPx - insetXPx;
				yPx = wt + (wh - heightPx) / 2;
				break;
			case Config.OsdScreenAnchor.BOTTOM_LEFT:
				xPx = wl + insetXPx;
				yPx = work.bottom - heightPx - insetYPx;
				break;
			case Config.OsdScreenAnchor.BOTTOM_CENTER:
				xPx = wl + (ww - widthPx) / 2;
				yPx = work.bottom - heightPx - insetYPx;
				break;
			default:
				xPx = work.right - widthPx - insetXPx;
				yPx = work.bottom - heightPx - insetYPx;
				break;
		}
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

	public void ShowPending(string rowLabel) {
		cancelPendingLevelUpdate();
		bool layoutChanged = updateLayoutKey(LayoutMode.BAR, rowLabel);
		hideExtraVisuals();
		applyLabel(rowLabel);
		BarFill.Width = 0;
		BarFill.Background = TRACK_BRUSH;
		BarFill.CornerRadius = new CornerRadius(_barHeight / 2.0, 0, 0, _barHeight / 2.0);
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

	public void ShowLevel(string rowLabel, double normalizedRatio, string displayText, bool volumeIncreased) {
		queueLevelUpdate(new PendingLevelUpdate(rowLabel, normalizedRatio, displayText, volumeIncreased));
	}

	void showLevelNow(string rowLabel, double normalizedRatio, string displayText, bool volumeIncreased) {
		bool layoutChanged = updateLayoutKey(LayoutMode.BAR, rowLabel);
		hideExtraVisuals();
		applyLabel(rowLabel);
		BarFill.Background = ACTIVE_BRUSH;
		updateBarFill(normalizedRatio);
		ValueTextBlock.Text = displayText;
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
		double radius = _barHeight / 2.0;
		BarFill.CornerRadius = fillWidth >= _trackWidth - 1
			? new CornerRadius(radius)
			: new CornerRadius(radius, 0, 0, radius);
	}

	void positionFlash(bool volumeIncreased) {
		double fillEndX = BarFill.Width;
		double outsideGap = _targetHeight * RATIO_FLASH_MARKER_OUTSIDE_GAP;
		double markerWidth = FlashMarker.Width;
		double insideGap = _targetHeight * RATIO_MIDDLE_COLUMN_RIGHT_MARGIN;
		double verticalOffset = 0;
		double x = volumeIncreased
			? Math.Min(Math.Max(0, fillEndX + outsideGap), Math.Max(0, _trackWidth - markerWidth))
			: Math.Min(Math.Max(0, fillEndX - markerWidth - insideGap), Math.Max(0, _trackWidth - markerWidth));
		FlashTransform.X = x;
		FlashTransform.Y = verticalOffset;
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
