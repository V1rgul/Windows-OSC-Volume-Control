namespace WindowsOscVolumeControl;

public sealed class BindingFader : BindingAbstract {
	static readonly HotkeyAction[] _prototypes = [
		new HotkeyActionFaderSet(),
		new HotkeyActionFaderDelta(),
	];

	public float minimum { get; set; } = 0f;
	public float maximum { get; set; } = 1f;

	public override IReadOnlyList<HotkeyAction> availableActionPrototypes => _prototypes;

	public BindingFader() { }

	public BindingFader(BindingFader other) : base(other) {
		minimum = FaderFloatUtil.RoundToBindingDecimals(other.minimum);
		maximum = FaderFloatUtil.RoundToBindingDecimals(other.maximum);
	}
}
