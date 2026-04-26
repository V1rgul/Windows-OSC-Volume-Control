namespace WindowsOscVolumeControl;

public sealed class BindingToggle : BindingAbstract {
	static readonly ControlAction[] _prototypes = [
		new ControlActionToggleSet(),
		new ControlActionToggleFlip(),
	];

	public override IReadOnlyList<ControlAction> availableActionPrototypes => _prototypes;

	public BindingToggle() { }

	public BindingToggle(BindingToggle other) : base(other) { }
}
