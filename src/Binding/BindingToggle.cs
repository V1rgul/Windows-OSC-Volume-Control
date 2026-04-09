namespace WindowsOscVolumeControl;

public sealed class BindingToggle : BindingAbstract {
	public HotkeyGesture hotkey { get; set; } = HotkeyGesture.None;

	public BindingToggle() { }

	public BindingToggle(BindingToggle other) : base(other) {
		hotkey = other.hotkey;
	}
}
