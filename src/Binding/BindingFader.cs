namespace WindowsOscVolumeControl;

public sealed class BindingFader : BindingAbstract {
	public float step { get; set; } = 0.02f;
	public float minimum { get; set; } = 0f;
	public float maximum { get; set; } = 1f;
	public HotkeyGesture hotkeyMinus { get; set; } = HotkeyGesture.None;
	public HotkeyGesture hotkeyPlus { get; set; } = HotkeyGesture.None;

	public BindingFader() { }

	public BindingFader(BindingFader other) : base(other) {
		step = FaderFloatUtil.RoundToBindingDecimals(other.step);
		minimum = FaderFloatUtil.RoundToBindingDecimals(other.minimum);
		maximum = FaderFloatUtil.RoundToBindingDecimals(other.maximum);
		hotkeyMinus = other.hotkeyMinus;
		hotkeyPlus = other.hotkeyPlus;
	}
}
