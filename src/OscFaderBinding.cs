using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class OscFaderBinding : OscBinding {
	public float step { get; set; } = 0.02f;
	public float minimum { get; set; } = 0f;
	public float maximum { get; set; } = 1f;
	public Keys hotkeyMinus { get; set; } = Keys.None;
	public Keys hotkeyPlus { get; set; } = Keys.None;

	public OscFaderBinding() { }

	public OscFaderBinding(OscFaderBinding other) : base(other) {
		step = FaderFloatUtil.RoundToBindingDecimals(other.step);
		minimum = FaderFloatUtil.RoundToBindingDecimals(other.minimum);
		maximum = FaderFloatUtil.RoundToBindingDecimals(other.maximum);
		hotkeyMinus = other.hotkeyMinus;
		hotkeyPlus = other.hotkeyPlus;
	}
}
