using System.Windows.Forms;

namespace X32VolumeHijacker;

public sealed class OscFaderBinding {
	public string Name { get; set; } = "";
	public string Address { get; set; } = "";
	public float Step { get; set; } = 0.02f;
	public float Minimum { get; set; } = 0f;
	public float Maximum { get; set; } = 1f;
	public Keys HotkeyMinus { get; set; } = Keys.None;
	public Keys HotkeyPlus { get; set; } = Keys.None;

	public OscFaderBinding() { }

	public OscFaderBinding(OscFaderBinding other) {
		ArgumentNullException.ThrowIfNull(other);
		Name = other.Name;
		Address = other.Address;
		Step = FaderFloatUtil.RoundToBindingDecimals(other.Step);
		Minimum = FaderFloatUtil.RoundToBindingDecimals(other.Minimum);
		Maximum = FaderFloatUtil.RoundToBindingDecimals(other.Maximum);
		HotkeyMinus = other.HotkeyMinus;
		HotkeyPlus = other.HotkeyPlus;
	}

	/// <summary>Default out-of-box row (cosmetic name only; not resolved by code).</summary>
	public static OscFaderBinding CreateDefaultMaster() => new() {
		Name = "MAIN",
		Address = "/main/st/mix/fader",
		Step = 0.02f,
		Minimum = 0f,
		Maximum = 1f,
		HotkeyMinus = Keys.VolumeDown,
		HotkeyPlus = Keys.VolumeUp,
	};
}
