using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class OscToggleBinding : OscBinding {
	public Keys hotkey { get; set; } = Keys.None;

	public OscToggleBinding() { }

	public OscToggleBinding(OscToggleBinding other) : base(other) {
		hotkey = other.hotkey;
	}
}
