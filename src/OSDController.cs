using System;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WindowsOscVolumeControl;

public class OSDController : Form
{
	const int OSD_FADE_MS = 200;
	const int OSD_FLASH_MS = 100;
	const double NORMAL_OPACITY = 0.85;

	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HWND_TOPMOST = new(-1);

	/// <summary>OSD size and dwell time; persisted via <see cref="WindowsOscVolumeControl.ConfigStore"/>.</summary>
	public sealed class Config {
		public const int MIN_HEIGHT_PX = 48;
		public const int MAX_HEIGHT_PX = 600;
		public const uint MIN_DISPLAY_DURATION_MS = 200;
		public const uint MAX_DISPLAY_DURATION_MS = 60_000;

		/// <summary>Total OSD client height in pixels (reference layout uses 78).</summary>
		public int HeightPx { get; set; } = 78;

		public uint DisplayDurationMs { get; set; } = 1000;

		public Config() { }

		public Config(Config other) {
			ArgumentNullException.ThrowIfNull(other);
			HeightPx = other.HeightPx;
			DisplayDurationMs = other.DisplayDurationMs;
		}

		public static Config Clamped(Config? c) {
			c ??= new Config();
			int h = Math.Clamp(c.HeightPx, MIN_HEIGHT_PX, MAX_HEIGHT_PX);
			uint d = c.DisplayDurationMs;
			if (d < MIN_DISPLAY_DURATION_MS) d = MIN_DISPLAY_DURATION_MS;
			if (d > MAX_DISPLAY_DURATION_MS) d = MAX_DISPLAY_DURATION_MS;
			return new Config { HeightPx = h, DisplayDurationMs = d };
		}
	}

	/// <summary>Reference total client height (24 + 30 + 24).</summary>
	const int REF_CLIENT_HEIGHT_PX = 78;
	const int REF_FRAME_MARGIN = 24;
	const int REF_BAR_HEIGHT = 30;
	const int REF_GAP_HORIZONTAL = 12;
	const int REF_TOGGLE_SYMBOL_DIAM = 24;
	const int REF_BASE_CLIENT_WIDTH = 420;
	const float REF_VALUE_FONT_PT = 12f;
	const float REF_FLASH_FONT_PT = 20f;
	const int REF_FLASH_GAP_PX = 4;

	readonly struct OsdLayoutMetrics
	{
		public int FrameMargin { get; init; }
		public int BarHeight { get; init; }
		public int GapHorizontal { get; init; }
		public int ToggleSymbolDiam { get; init; }
		public int BaseClientWidth { get; init; }
		public float ValueFontEmSize { get; init; }
		public float FlashFontEmSize { get; init; }
		public int FlashGapPx { get; init; }
		public float ToggleEllipsePenWidth { get; init; }
		public int NameColumnMinWidth { get; init; }
		public int NameColumnExtraPad { get; init; }
		public int MinBarWidth { get; init; }

		public int ClientHeightPx => FrameMargin + BarHeight + FrameMargin;

		public static OsdLayoutMetrics FromHeightPx(int heightPx)
		{
			double scale = heightPx / (double)REF_CLIENT_HEIGHT_PX;
			int margin = Math.Max(4, (int)Math.Round(REF_FRAME_MARGIN * scale));
			int bar = heightPx - 2 * margin;
			if (bar < 1) {
				margin = Math.Max(0, (heightPx - 1) / 2);
				bar = heightPx - 2 * margin;
				bar = Math.Max(1, bar);
			}
			int gap = Math.Max(4, (int)Math.Round(REF_GAP_HORIZONTAL * scale));
			int toggle = Math.Max(8, (int)Math.Round(REF_TOGGLE_SYMBOL_DIAM * scale));
			int baseW = Math.Max(200, (int)Math.Round(REF_BASE_CLIENT_WIDTH * scale));
			float valuePt = Math.Max(6f, (float)(REF_VALUE_FONT_PT * scale));
			float flashPt = Math.Max(8f, (float)(REF_FLASH_FONT_PT * scale));
			int flashGap = Math.Max(1, (int)Math.Round(REF_FLASH_GAP_PX * scale));
			float penW = Math.Max(1f, (float)(2.0 * scale));
			int nameMin = Math.Max(8, (int)Math.Round(40 * scale));
			int namePad = Math.Max(2, (int)Math.Round(8 * scale));
			int minBar = Math.Max(40, (int)Math.Round(80 * scale));
			return new OsdLayoutMetrics {
				FrameMargin = margin,
				BarHeight = bar,
				GapHorizontal = gap,
				ToggleSymbolDiam = toggle,
				BaseClientWidth = baseW,
				ValueFontEmSize = valuePt,
				FlashFontEmSize = flashPt,
				FlashGapPx = flashGap,
				ToggleEllipsePenWidth = penW,
				NameColumnMinWidth = nameMin,
				NameColumnExtraPad = namePad,
				MinBarWidth = minBar,
			};
		}
	}

	sealed class CachedLayout : IDisposable
	{
		public Font ValueFont { get; }
		public Font FlashFont { get; }
		public int NameColumnWidth { get; }
		public int BarLeft { get; }
		public int BarWidth { get; }
		public int ComputedClientWidth { get; }
		public SizeF PlusFlashSize { get; }
		public SizeF MinusFlashSize { get; }

		public CachedLayout(Graphics g, OsdLayoutMetrics m, string rowLabelForMeasure, int osdValueFractionalDigits, bool toggleCompact)
		{
			ValueFont = new Font("Segoe UI", m.ValueFontEmSize, FontStyle.Bold);
			FlashFont = new Font("Segoe UI", m.FlashFontEmSize, FontStyle.Bold);
			string label = string.IsNullOrWhiteSpace(rowLabelForMeasure) ? "" : rowLabelForMeasure.Trim();
			int nameW = 0;
			if (label.Length > 0) {
				const int MAX_CHARS = 48;
				if (label.Length > MAX_CHARS)
					label = label[..MAX_CHARS] + "…";
				nameW = (int)Math.Ceiling(g.MeasureString(label, ValueFont).Width);
				if (!toggleCompact) {
					nameW += m.NameColumnExtraPad;
					nameW = Math.Max(nameW, m.NameColumnMinWidth);
				}
			}
			NameColumnWidth = nameW;
			if (toggleCompact) {
				BarLeft = m.FrameMargin + NameColumnWidth;
				BarWidth = 0;
				ComputedClientWidth = m.FrameMargin + NameColumnWidth + m.GapHorizontal + m.ToggleSymbolDiam + m.FrameMargin;
			} else {
				osdValueFractionalDigits = Math.Clamp(osdValueFractionalDigits, 0, Math.Max(0, FaderFloatUtil.BindingFractionalDigits));
				string valueMeasure = FaderFloatUtil.OsdMeasureSample(osdValueFractionalDigits);
				int reserve = (int)Math.Ceiling(g.MeasureString(valueMeasure, ValueFont).Width);
				int nameGap = NameColumnWidth > 0 ? m.GapHorizontal : 0;
				int barLeft = m.FrameMargin + NameColumnWidth + nameGap;
				BarLeft = barLeft;
				int barPreferred = m.BaseClientWidth - m.FrameMargin - m.FrameMargin - m.GapHorizontal - reserve;
				if (barPreferred < m.MinBarWidth)
					barPreferred = m.MinBarWidth;
				BarWidth = barPreferred;
				ComputedClientWidth = barLeft + BarWidth + m.GapHorizontal + reserve + m.FrameMargin;
			}
			PlusFlashSize = g.MeasureString("+", FlashFont);
			MinusFlashSize = g.MeasureString("−", FlashFont);
		}

		public void Dispose()
		{
			ValueFont.Dispose();
			FlashFont.Dispose();
		}
	}

	enum FlashSign
	{
		NONE,
		PLUS,
		MINUS,
	}

	enum OsdView
	{
		LEVEL,
		PENDING,
		ERROR,
		TOGGLE_STATUS,
	}

	System.Windows.Forms.Timer _autoHideTimer;
	System.Windows.Forms.Timer _fadeTimer;
	System.Windows.Forms.Timer _flashTimer;
	long _fadeStartTick;
	float _levelRaw;
	float _levelMin;
	float _levelMax;
	int _levelFracDigits = 2;
	OsdView _view = OsdView.LEVEL;
	FlashSign _flashSign;
	string _rowLabel = "";
	string _statusText = "";
	bool _statusOn;

	OsdLayoutMetrics _metrics;
	int _displayIntervalMs;

	CachedLayout? _cache;

	protected override bool ShowWithoutActivation => true;

	protected override CreateParams CreateParams
	{
		get
		{
			var cp = base.CreateParams;
			cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
			return cp;
		}
	}

	public OSDController(Config osdConfig)
	{
		ArgumentNullException.ThrowIfNull(osdConfig);
		Config d = Config.Clamped(osdConfig);
		_metrics = OsdLayoutMetrics.FromHeightPx(d.HeightPx);
		_displayIntervalMs = (int)Math.Min(d.DisplayDurationMs, int.MaxValue);

		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.Manual;
		TopMost = true;
		ShowInTaskbar = false;
		ClientSize = new Size(_metrics.BaseClientWidth, _metrics.ClientHeightPx);
		BackColor = Color.Black;
		Opacity = NORMAL_OPACITY;
		DoubleBuffered = true;

		RepositionTrailingEdge();

		_fadeTimer = new System.Windows.Forms.Timer();
		_fadeTimer.Tick += (s, e) =>
		{
			double progress = (Environment.TickCount64 - _fadeStartTick) / (double)OSD_FADE_MS;
			if (progress >= 1.0) {
				_fadeTimer.Stop();
				Hide();
				Opacity = NORMAL_OPACITY;
			} else {
				Opacity = NORMAL_OPACITY * (1.0 - progress);
			}
		};

		_flashTimer = new System.Windows.Forms.Timer();
		_flashTimer.Interval = OSD_FLASH_MS;
		_flashTimer.Tick += (s, e) =>
		{
			_flashSign = FlashSign.NONE;
			_flashTimer.Stop();
			Invalidate();
		};

		_autoHideTimer = new System.Windows.Forms.Timer();
		_autoHideTimer.Interval = _displayIntervalMs;
		_autoHideTimer.Tick += (s, e) =>
		{
			_autoHideTimer.Stop();
			_fadeStartTick = Environment.TickCount64;
			_fadeTimer.Interval = Math.Max(1, 1000 / GetScreenRefreshRate());
			_fadeTimer.Start();
		};
	}

	/// <summary>Updates layout metrics, client size, auto-hide interval, and reposition. Safe before handle creation.</summary>
	public void ApplyConfig(Config? cfg)
	{
		Config c = Config.Clamped(cfg);
		_metrics = OsdLayoutMetrics.FromHeightPx(c.HeightPx);
		_displayIntervalMs = (int)Math.Min(c.DisplayDurationMs, int.MaxValue);
		_autoHideTimer.Interval = Math.Max(1, _displayIntervalMs);
		if (IsHandleCreated) {
			RebuildLayoutCache();
			Invalidate();
		}
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		RebuildLayoutCache();
	}

	protected override void OnDpiChanged(DpiChangedEventArgs e)
	{
		base.OnDpiChanged(e);
		RebuildLayoutCache();
		Invalidate();
	}

	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		_cache?.Dispose();
		_cache = null;
		base.OnFormClosed(e);
	}

	string LayoutMeasureLabel() => _view switch {
		OsdView.TOGGLE_STATUS => _statusText,
		OsdView.LEVEL or OsdView.PENDING => _rowLabel,
		_ => "",
	};

	int LayoutValueFractionalDigits() => _view switch {
		OsdView.LEVEL => _levelFracDigits,
		OsdView.PENDING => _levelFracDigits,
		_ => Math.Max(0, FaderFloatUtil.BindingFractionalDigits),
	};

	void RepositionTrailingEdge()
	{
		var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
		int m = _metrics.FrameMargin;
		int x = workingArea.Width - Width - m;
		int y = workingArea.Height - Height - m;
		Location = new Point(x, y);
	}

	void RebuildLayoutCache()
	{
		_cache?.Dispose();
		using var g = CreateGraphics();
		_cache = new CachedLayout(g, _metrics, LayoutMeasureLabel(), LayoutValueFractionalDigits(), _view == OsdView.TOGGLE_STATUS);
		int h = _metrics.ClientHeightPx;
		if (ClientSize.Width != _cache.ComputedClientWidth || ClientSize.Height != h)
			ClientSize = new Size(_cache.ComputedClientWidth, h);
		RepositionTrailingEdge();
	}

	void ShowNoActivate()
	{
		_autoHideTimer.Stop();
		_fadeTimer.Stop();
		Opacity = NORMAL_OPACITY;
		_ = Handle;
		RebuildLayoutCache();
		SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
		_autoHideTimer.Start();
	}

	public void ShowPending() => ShowPending("", float.NaN);

	public void ShowPending(string rowLabel) => ShowPending(rowLabel, float.NaN);

	/// <param name="faderStepForLayout">If finite and &gt; 0, value column width matches this step’s OSD decimals (fader pending).</param>
	public void ShowPending(string rowLabel, float faderStepForLayout)
	{
		if (float.IsFinite(faderStepForLayout) && faderStepForLayout > 0f)
			_levelFracDigits = FaderFloatUtil.GetOsdFractionalDigitsFromStep(faderStepForLayout);
		_flashTimer.Stop();
		_flashSign = FlashSign.NONE;
		_rowLabel = rowLabel ?? "";
		_view = OsdView.PENDING;
		Invalidate();
		ShowNoActivate();
	}

	public void ShowError()
	{
		_flashTimer.Stop();
		_flashSign = FlashSign.NONE;
		_view = OsdView.ERROR;
		Invalidate();
		ShowNoActivate();
	}

	/// <param name="volumeIncreased">If true, a brief + appears right of the fill; if false, a brief − appears left of the fill.</param>
	/// <param name="step">Binding step (after rounding); drives value/% decimal places on the OSD.</param>
	public void ShowLevel(string rowLabel, float min, float max, float value, bool volumeIncreased, float step)
	{
		_rowLabel = rowLabel ?? "";
		_levelMin = min;
		_levelMax = max;
		_levelRaw = value;
		_levelFracDigits = FaderFloatUtil.GetOsdFractionalDigitsFromStep(step);
		_view = OsdView.LEVEL;
		_flashSign = volumeIncreased ? FlashSign.PLUS : FlashSign.MINUS;
		_flashTimer.Stop();
		_flashTimer.Start();
		Invalidate();
		ShowNoActivate();
	}

	/// <summary>Shows on/off state for an OSC toggle; <paramref name="displayName"/> is normally <see cref="OscBindingAbstract.displayName"/> from the caller so this layer stays free of binding types.</summary>
	/// <param name="displayName">Row label (same rules as <see cref="OscBindingAbstract.displayName"/>).</param>
	/// <param name="enabled">Whether the toggle is on.</param>
	public void ShowToggle(string displayName, bool enabled)
	{
		_flashTimer.Stop();
		_flashSign = FlashSign.NONE;
		_statusText = displayName;
		_statusOn = enabled;
		_view = OsdView.TOGGLE_STATUS;
		Invalidate();
		ShowNoActivate();
	}

	static float Normalized01(float value, float min, float max)
	{
		if (max - min < 1e-9f)
			return 0.5f;
		float t = (value - min) / (max - min);
		return Math.Clamp(t, 0f, 1f);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		var g = e.Graphics;
		var c = _cache;
		if (c == null)
			return;

		int fm = _metrics.FrameMargin;
		int bh = _metrics.BarHeight;
		int gap = _metrics.FlashGapPx;

		if (_view == OsdView.ERROR) {
			DrawStatusCentered(g, ClientSize, c.ValueFont, "ERROR", Brushes.Gray);
			return;
		}

		if (_view == OsdView.TOGGLE_STATUS) {
			DrawToggleStatus(g, c);
			return;
		}

		int barW = c.BarWidth;
		int barLeft = c.BarLeft;

		if (c.NameColumnWidth > 0 && !string.IsNullOrWhiteSpace(_rowLabel)) {
			using var nameSf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
			var nameRect = new RectangleF(fm, fm, c.NameColumnWidth, bh);
			nameSf.Alignment = StringAlignment.Near;
			g.DrawString(_rowLabel.Trim(), c.ValueFont, Brushes.White, nameRect, nameSf);
		}

		if (_view == OsdView.PENDING) {
			g.FillRectangle(Brushes.Gray, barLeft, fm, barW, bh);
			DrawValueInColumn(g, c.ValueFont, "—", barLeft, barW);
			return;
		}

		float norm = Normalized01(_levelRaw, _levelMin, _levelMax);
		g.FillRectangle(Brushes.Gray, barLeft, fm, barW, bh);
		float fillW = barW * norm;
		g.FillRectangle(Brushes.LimeGreen, barLeft, fm, fillW, bh);

		string valueText = FaderFloatUtil.FormatOsdLevelValue(_levelRaw, _levelFracDigits);
		DrawValueInColumn(g, c.ValueFont, valueText, barLeft, barW);

		if (_flashSign != FlashSign.NONE) {
			float endX = barLeft + fillW;
			int centerY = fm + bh / 2;
			if (_flashSign == FlashSign.PLUS) {
				float x = endX + gap;
				float y = centerY - c.PlusFlashSize.Height / 2f;
				g.DrawString("+", c.FlashFont, Brushes.Black, x, y);
			} else {
				float x = endX - gap - c.MinusFlashSize.Width;
				float y = centerY - c.MinusFlashSize.Height / 2f;
				g.DrawString("−", c.FlashFont, Brushes.Black, x, y);
			}
		}
	}

	void DrawToggleStatus(Graphics g, CachedLayout c)
	{
		int fm = _metrics.FrameMargin;
		int bh = _metrics.BarHeight;
		int gapH = _metrics.GapHorizontal;
		int diam = _metrics.ToggleSymbolDiam;
		float penW = _metrics.ToggleEllipsePenWidth;

		int nameCol = c.NameColumnWidth;
		if (nameCol > 0) {
			using var nameSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
			var nameRect = new RectangleF(fm, fm, nameCol, bh);
			g.DrawString(_statusText, c.ValueFont, _statusOn ? Brushes.LimeGreen : Brushes.Red, nameRect, nameSf);
		} else {
			float textW = Math.Max(1f, c.BarLeft - fm - gapH);
			var textRect = new RectangleF(fm, fm, textW, bh);
			using var textSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
			g.DrawString(_statusText, c.ValueFont, _statusOn ? Brushes.LimeGreen : Brushes.Red, textRect, textSf);
		}

		int symLeft = ClientSize.Width - fm - diam;
		int symTop = fm + (bh - diam) / 2;
		var symBounds = new Rectangle(symLeft, symTop, diam, diam);

		var prevSmooth = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		try {
			if (_statusOn) {
				g.FillEllipse(Brushes.LimeGreen, symBounds);
			} else {
				using var redPen = new Pen(Color.Red, penW);
				var strokeRect = Rectangle.Inflate(symBounds, -1, -1);
				g.DrawEllipse(redPen, strokeRect);
				float inset = diam * 0.22f;
				g.DrawLine(
					redPen,
					symLeft + inset,
					symTop + diam - inset,
					symLeft + diam - inset,
					symTop + inset);
			}
		} finally {
			g.SmoothingMode = prevSmooth;
		}
	}

	static void DrawStatusCentered(Graphics g, Size clientSize, Font font, string text, Brush brush)
	{
		var rect = new RectangleF(0, 0, clientSize.Width, clientSize.Height);
		using var sf = new StringFormat {
			Alignment = StringAlignment.Center,
			LineAlignment = StringAlignment.Center,
		};
		g.DrawString(text, font, brush, rect, sf);
	}

	void DrawValueInColumn(Graphics g, Font font, string text, int barLeft, int barW)
	{
		int fm = _metrics.FrameMargin;
		int bh = _metrics.BarHeight;
		int gh = _metrics.GapHorizontal;
		float left = barLeft + barW + gh;
		float w = ClientSize.Width - fm - left;
		if (w < 1f)
			return;
		var rect = new RectangleF(left, fm, w, bh);
		using var sf = new StringFormat();
		sf.Alignment = StringAlignment.Far;
		sf.LineAlignment = StringAlignment.Center;
		g.DrawString(text, font, Brushes.White, rect, sf);
	}

	static int GetScreenRefreshRate()
	{
		var dm = new DEVMODE();
		dm.dmSize = (short)Marshal.SizeOf<DEVMODE>();
		if (EnumDisplaySettings(null, -1, ref dm) && dm.dmDisplayFrequency > 0)
			return dm.dmDisplayFrequency;
		return 60;
	}

	[StructLayout(LayoutKind.Explicit, CharSet = CharSet.Auto, Size = 220)]
	struct DEVMODE
	{
		[FieldOffset(68)]
		public short dmSize;
		[FieldOffset(184)]
		public int dmDisplayFrequency;
	}

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

	[DllImport("user32.dll")]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
