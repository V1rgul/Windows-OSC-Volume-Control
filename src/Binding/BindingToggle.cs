namespace WindowsOscVolumeControl;

public sealed class BindingToggle : BindingAbstract {
	static readonly HotkeyAction[] _prototypes = [
		new HotkeyActionToggleSet(),
		new HotkeyActionToggleFlip(),
	];

	public override IReadOnlyList<HotkeyAction> availableActionPrototypes => _prototypes;

	public BindingToggle() { }

	public BindingToggle(BindingToggle other) : base(other) { }
}
