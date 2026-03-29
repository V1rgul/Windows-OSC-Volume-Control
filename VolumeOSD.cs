using System;
using System.Runtime.InteropServices;

public class VolumeOsd : Form
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

    /// <summary>Uniform black frame inset on all sides (client area and gap to screen edges).</summary>
    const int FRAME_MARGIN = 24;
    const int BAR_HEIGHT = 30;
    const int GAP_BAR_TO_VALUE = 8;

    /// <summary>Fonts and layout metrics; rebuilt when the form loads or DPI changes.</summary>
    sealed class CachedLayout : IDisposable
    {
        public Font ValueFont { get; }
        public Font FlashFont { get; }
        public int BarWidth { get; }
        public SizeF PlusFlashSize { get; }
        public SizeF MinusFlashSize { get; }

        public CachedLayout(VolumeOsd form, Graphics g)
        {
            ValueFont = new Font("Segoe UI", 12, FontStyle.Bold);
            FlashFont = new Font("Segoe UI", 20, FontStyle.Bold);
            var client = form.ClientSize;
            int reserve = (int)Math.Ceiling(g.MeasureString("100.00%", ValueFont).Width);
            int barW = client.Width - FRAME_MARGIN - FRAME_MARGIN - GAP_BAR_TO_VALUE - reserve;
            if (barW < 80)
                barW = 80;
            BarWidth = barW;
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
        Mute,
        Unmute,
        ToggleStatus,
    }


    System.Windows.Forms.Timer _autoHideTimer;
    System.Windows.Forms.Timer _fadeTimer;
    System.Windows.Forms.Timer _flashTimer;
    long _fadeStartTick;
    float _level = 0;
    OsdView _view = OsdView.Level;
    FlashSign _flashSign;
    string _statusText = "";
    bool _statusOn;

    CachedLayout? _cache;

    /// <summary>Keep overlay from activating; otherwise fullscreen focus is lost and the taskbar / media chrome can appear.</summary>
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

    public VolumeOsd()
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

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        int x = workingArea.Width - Width - FRAME_MARGIN;
        int y = workingArea.Height - Height - FRAME_MARGIN;
        Location = new Point(x, y);

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
            if (progress >= 1.0)
            {
                _fadeTimer.Stop();
                Hide();
                Opacity = NORMAL_OPACITY;
            }
            else
            {
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

    void RebuildLayoutCache()
    {
        _cache?.Dispose();
        using var g = CreateGraphics();
        _cache = new CachedLayout(this, g);
    }

    void ShowNoActivate()
    {
        _autoHideTimer.Stop();
        _fadeTimer.Stop();
        Opacity = NORMAL_OPACITY;
        _ = Handle;
        if (_cache == null)
            RebuildLayoutCache();
        SetWindowPos(Handle, HWND_TOPMOST, Left, Top, Width, Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        _autoHideTimer.Start();
    }

    public void ShowPending()
    {
        _flashTimer.Stop();
        _flashSign = FlashSign.None;
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
    public void ShowLevel(float value, bool volumeIncreased)
    {
        _view = OsdView.Level;
        _level = value;
        _flashSign = volumeIncreased ? FlashSign.Plus : FlashSign.Minus;
        _flashTimer.Stop();
        _flashTimer.Start();
        Invalidate();
        ShowNoActivate();
    }

    public void ShowMute(bool muted)
    {
        _flashTimer.Stop();
        _flashSign = FlashSign.None;
        _view = muted ? OsdView.Mute : OsdView.Unmute;
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

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var c = _cache;
        if (c == null)
            return;

        int barW = c.BarWidth;

        if (_view == OsdView.Error)
        {
            DrawStatusCentered(g, ClientSize, c.ValueFont, "ERROR", Brushes.Gray);
            return;
        }

        if (_view == OsdView.Mute)
        {
            DrawStatusCentered(g, ClientSize, c.ValueFont, "MUTE", Brushes.Red);
            return;
        }

        if (_view == OsdView.Unmute)
        {
            DrawStatusCentered(g, ClientSize, c.ValueFont, "UNMUTE", Brushes.LimeGreen);
            return;
        }

        if (_view == OsdView.ToggleStatus)
        {
            DrawStatusCentered(g, ClientSize, c.ValueFont, _statusText, _statusOn ? Brushes.LimeGreen : Brushes.Red);
            return;
        }

        if (_view == OsdView.Pending)
        {
            g.FillRectangle(Brushes.Gray, FRAME_MARGIN, FRAME_MARGIN, barW, BAR_HEIGHT);
            DrawValueInColumn(g, c.ValueFont, "—", barW);
            return;
        }

        g.FillRectangle(Brushes.Gray, FRAME_MARGIN, FRAME_MARGIN, barW, BAR_HEIGHT);
        float fillW = barW * Math.Clamp(_level, 0f, 1f);
        g.FillRectangle(Brushes.LimeGreen, FRAME_MARGIN, FRAME_MARGIN, fillW, BAR_HEIGHT);

        float pct = Math.Clamp(_level, 0f, 1f) * 100f;
        DrawValueInColumn(g, c.ValueFont, FormattableString.Invariant($"{pct:F2}%"), barW);

        if (_flashSign != FlashSign.None)
        {
            float endX = FRAME_MARGIN + fillW;
            int centerY = FRAME_MARGIN + BAR_HEIGHT / 2;
            const int gap = 4;
            if (_flashSign == FlashSign.Plus)
            {
                float x = endX + gap;
                float y = centerY - c.PlusFlashSize.Height / 2f;
                g.DrawString("+", c.FlashFont, Brushes.Black, x, y);
            }
            else
            {
                float x = endX - gap - c.MinusFlashSize.Width;
                float y = centerY - c.MinusFlashSize.Height / 2f;
                g.DrawString("−", c.FlashFont, Brushes.Black, x, y);
            }
        }
    }

    static void DrawStatusCentered(Graphics g, Size clientSize, Font font, string text, Brush brush)
    {
        var rect = new RectangleF(0, 0, clientSize.Width, clientSize.Height);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(text, font, brush, rect, sf);
    }

    void DrawValueInColumn(Graphics g, Font font, string text, int barW)
    {
        float left = FRAME_MARGIN + barW + GAP_BAR_TO_VALUE;
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
