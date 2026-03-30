using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class OscFaderBinding {
	public string name { get; set; } = "";
	public string address { get; set; } = "";
	public float step { get; set; } = 0.02f;
	public float minimum { get; set; } = 0f;
	public float maximum { get; set; } = 1f;
	public Keys hotkeyMinus { get; set; } = Keys.None;
	public Keys hotkeyPlus { get; set; } = Keys.None;

	public OscFaderBinding() { }

	public OscFaderBinding(OscFaderBinding other) {
		ArgumentNullException.ThrowIfNull(other);
		name = other.name;
		address = other.address;
		step = FaderFloatUtil.RoundToBindingDecimals(other.step);
		minimum = FaderFloatUtil.RoundToBindingDecimals(other.minimum);
		maximum = FaderFloatUtil.RoundToBindingDecimals(other.maximum);
		hotkeyMinus = other.hotkeyMinus;
		hotkeyPlus = other.hotkeyPlus;
	}

	/// <summary>Default out-of-box row (cosmetic name only; not resolved by code).</summary>
	public static OscFaderBinding createDefaultMaster() => new() {
		name = "MAIN",
		address = "/main/st/mix/fader",
		step = 0.02f,
		minimum = 0f,
		maximum = 1f,
		hotkeyMinus = Keys.VolumeDown,
		hotkeyPlus = Keys.VolumeUp,
	};
}
