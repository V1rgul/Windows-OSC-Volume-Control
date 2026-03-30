using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class OscBindingToggle : OscBindingAbstract {
	public Keys hotkey { get; set; } = Keys.None;

	public OscBindingToggle() { }

	public OscBindingToggle(OscBindingToggle other) : base(other) {
		hotkey = other.hotkey;
	}
}
