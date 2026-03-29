using System;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using WindowsOscVolumeControl;

public class OSDController : Form
{

	const int OSD_DISPLAY_MS = 1000;
	const int OSD_FADE_MS = 200;
	const int OSD_FLASH_MS = 100;
	const double NORMAL_OPACITY = 0.85;

	const int WS_EX_TOOLWINDOW = 0x00000080;
	const int WS_EX_NOACTIVATE = 0x08000000;
	const uint SWP_NOACTIVATE = 0x0010;
	const uint SWP_SHOWWINDOW = 0x0040;
	static readonly IntPtr HWND_TOPMOST = new(-1);

	const int FRAME_MARGIN = 24;
	const int BAR_HEIGHT = 30;
	const int GAP_BAR_TO_VALUE = 8;
	const int GAP_NAME_TO_BAR = 8;
	const int TOGGLE_SYMBOL_DIAM = 22;
	const int OSD_BASE_CLIENT_WIDTH = 420;

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

		public CachedLayout(Graphics g, string rowLabelForMeasure, int osdValueFractionalDigits)
		{
			ValueFont = new Font("Segoe UI", 12, FontStyle.Bold);
			FlashFont = new Font("Segoe UI", 20, FontStyle.Bold);
			string label = string.IsNullOrWhiteSpace(rowLabelForMeasure) ? "" : rowLabelForMeasure.Trim();
			int nameW = 0;
			if (label.Length > 0) {
				const int maxChars = 48;
				if (label.Length > maxChars)
					label = label[..maxChars] + "…";
				nameW = (int)Math.Ceiling(g.MeasureString(label, ValueFont).Width) + 8;
				nameW = Math.Max(nameW, 40);
			}
			NameColumnWidth = nameW;
			osdValueFractionalDigits = Math.Clamp(osdValueFractionalDigits, 0, Math.Max(0, FaderFloatUtil.BindingFractionalDigits));
			string valueMeasure = FaderFloatUtil.OsdMeasureSample(osdValueFractionalDigits);
			int reserve = (int)Math.Ceiling(g.MeasureString(valueMeasure, ValueFont).Width);
			int nameGap = NameColumnWidth > 0 ? GAP_NAME_TO_BAR : 0;
			int barLeft = FRAME_MARGIN + NameColumnWidth + nameGap;
			BarLeft = barLeft;
			int barPreferred = OSD_BASE_CLIENT_WIDTH - FRAME_MARGIN - FRAME_MARGIN - GAP_BAR_TO_VALUE - reserve;
			if (barPreferred < 80)
				barPreferred = 80;
			BarWidth = barPreferred;
			ComputedClientWidth = barLeft + BarWidth + GAP_BAR_TO_VALUE + reserve + FRAME_MARGIN;
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
		None,
		Plus,
		Minus,
	}

	enum OsdView
	{
		Level,
		Pending,
		Error,
		ToggleStatus,
	}

	System.Windows.Forms.Timer _autoHideTimer;
	System.Windows.Forms.Timer _fadeTimer;
	System.Windows.Forms.Timer _flashTimer;
	long _fadeStartTick;
	float _levelRaw;
	float _levelMin;
	float _levelMax;
	int _levelFracDigits = 2;
	OsdView _view = OsdView.Level;
	FlashSign _flashSign;
	string _rowLabel = "";
	string _statusText = "";
	bool _statusOn;

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

	public OSDController()
	{
		FormBorderStyle = FormBorderStyle.None;
		StartPosition = FormStartPosition.Manual;
		TopMost = true;
		ShowInTaskbar = false;
		Width = 420;
		Height = FRAME_MARGIN + BAR_HEIGHT + FRAME_MARGIN;
		BackColor = Color.Black;
		Opacity = NORMAL_OPACITY;
		DoubleBuffered = true;

		RepositionTrailingEdge();

		_autoHideTimer = new System.Windows.Forms.Timer();
		_autoHideTimer.Interval = OSD_DISPLAY_MS;
		_autoHideTimer.Tick += (s, e) =>
		{
			_autoHideTimer.Stop();
			_fadeStartTick = Environment.TickCount64;
			_fadeTimer.Interval = Math.Max(1, 1000 / GetScreenRefreshRate());
			_fadeTimer.Start();
		};

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
			_flashSign = FlashSign.None;
			_flashTimer.Stop();
			Invalidate();
		};
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
		OsdView.ToggleStatus => _statusText,
		OsdView.Level or OsdView.Pending => _rowLabel,
		_ => "",
	};

	int LayoutValueFractionalDigits() => _view switch {
		OsdView.Level => _levelFracDigits,
		OsdView.Pending => _levelFracDigits,
		_ => Math.Max(0, FaderFloatUtil.BindingFractionalDigits),
	};

	void RepositionTrailingEdge()
	{
		var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
		int x = workingArea.Width - Width - FRAME_MARGIN;
		int y = workingArea.Height - Height - FRAME_MARGIN;
		Location = new Point(x, y);
	}

	void RebuildLayoutCache()
	{
		_cache?.Dispose();
		using var g = CreateGraphics();
		_cache = new CachedLayout(g, LayoutMeasureLabel(), LayoutValueFractionalDigits());
		int h = FRAME_MARGIN + BAR_HEIGHT + FRAME_MARGIN;
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
		_flashSign = FlashSign.None;
		_rowLabel = rowLabel ?? "";
		_view = OsdView.Pending;
		Invalidate();
		ShowNoActivate();
	}

	public void ShowError()
	{
		_flashTimer.Stop();
		_flashSign = FlashSign.None;
		_view = OsdView.Error;
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
		_view = OsdView.Level;
		_flashSign = volumeIncreased ? FlashSign.Plus : FlashSign.Minus;
		_flashTimer.Stop();
		_flashTimer.Start();
		Invalidate();
		ShowNoActivate();
	}

	public void ShowToggle(string name, bool enabled)
	{
		_flashTimer.Stop();
		_flashSign = FlashSign.None;
		_statusText = string.IsNullOrWhiteSpace(name) ? "OSC TOGGLE" : name.Trim();
		_statusOn = enabled;
		_view = OsdView.ToggleStatus;
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

		if (_view == OsdView.Error) {
			DrawStatusCentered(g, ClientSize, c.ValueFont, "ERROR", Brushes.Gray);
			return;
		}

		if (_view == OsdView.ToggleStatus) {
			DrawToggleStatus(g, c);
			return;
		}

		int barW = c.BarWidth;
		int barLeft = c.BarLeft;

		if (c.NameColumnWidth > 0 && !string.IsNullOrWhiteSpace(_rowLabel)) {
			using var nameSf = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
			var nameRect = new RectangleF(FRAME_MARGIN, FRAME_MARGIN, c.NameColumnWidth, BAR_HEIGHT);
			nameSf.Alignment = StringAlignment.Near;
			g.DrawString(_rowLabel.Trim(), c.ValueFont, Brushes.White, nameRect, nameSf);
		}

		if (_view == OsdView.Pending) {
			g.FillRectangle(Brushes.Gray, barLeft, FRAME_MARGIN, barW, BAR_HEIGHT);
			DrawValueInColumn(g, c.ValueFont, "—", barLeft, barW);
			return;
		}

		float norm = Normalized01(_levelRaw, _levelMin, _levelMax);
		g.FillRectangle(Brushes.Gray, barLeft, FRAME_MARGIN, barW, BAR_HEIGHT);
		float fillW = barW * norm;
		g.FillRectangle(Brushes.LimeGreen, barLeft, FRAME_MARGIN, fillW, BAR_HEIGHT);

		string valueText = FaderFloatUtil.FormatOsdLevelValue(_levelRaw, _levelFracDigits);
		DrawValueInColumn(g, c.ValueFont, valueText, barLeft, barW);

		if (_flashSign != FlashSign.None) {
			float endX = barLeft + fillW;
			int centerY = FRAME_MARGIN + BAR_HEIGHT / 2;
			const int gap = 4;
			if (_flashSign == FlashSign.Plus) {
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
		int nameCol = c.NameColumnWidth;
		if (nameCol > 0) {
			using var nameSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
			var nameRect = new RectangleF(FRAME_MARGIN, FRAME_MARGIN, nameCol, BAR_HEIGHT);
			g.DrawString(_statusText, c.ValueFont, _statusOn ? Brushes.LimeGreen : Brushes.Red, nameRect, nameSf);
		} else {
			float textW = Math.Max(1f, c.BarLeft - FRAME_MARGIN - GAP_BAR_TO_VALUE);
			var textRect = new RectangleF(FRAME_MARGIN, FRAME_MARGIN, textW, BAR_HEIGHT);
			using var textSf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
			g.DrawString(_statusText, c.ValueFont, _statusOn ? Brushes.LimeGreen : Brushes.Red, textRect, textSf);
		}

		int symLeft = ClientSize.Width - FRAME_MARGIN - TOGGLE_SYMBOL_DIAM;
		int symTop = FRAME_MARGIN + (BAR_HEIGHT - TOGGLE_SYMBOL_DIAM) / 2;
		var symBounds = new Rectangle(symLeft, symTop, TOGGLE_SYMBOL_DIAM, TOGGLE_SYMBOL_DIAM);

		var prevSmooth = g.SmoothingMode;
		g.SmoothingMode = SmoothingMode.AntiAlias;
		try {
			if (_statusOn) {
				g.FillEllipse(Brushes.LimeGreen, symBounds);
			} else {
				const float penW = 2f;
				using var redPen = new Pen(Color.Red, penW);
				var strokeRect = Rectangle.Inflate(symBounds, -1, -1);
				g.DrawEllipse(redPen, strokeRect);
				float inset = TOGGLE_SYMBOL_DIAM * 0.22f;
				g.DrawLine(
					redPen,
					symLeft + inset,
					symTop + TOGGLE_SYMBOL_DIAM - inset,
					symLeft + TOGGLE_SYMBOL_DIAM - inset,
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
		float left = barLeft + barW + GAP_BAR_TO_VALUE;
		float w = ClientSize.Width - FRAME_MARGIN - left;
		if (w < 1f)
			return;
		var rect = new RectangleF(left, FRAME_MARGIN, w, BAR_HEIGHT);
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
