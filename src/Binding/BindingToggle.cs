using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class BindingToggle : BindingAbstract {
	public Keys hotkey { get; set; } = Keys.None;

	public BindingToggle() { }

	public BindingToggle(BindingToggle other) : base(other) {
		hotkey = other.hotkey;
	}
}
