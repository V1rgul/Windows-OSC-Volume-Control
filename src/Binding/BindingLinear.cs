namespace WindowsOscVolumeControl;

public sealed class BindingLinear : BindingFloatAbstract {
	static readonly ControlAction[] _prototypes = [
		new ControlActionContinuousSet(),
		new ControlActionContinuousDelta(),
	];

	public override IReadOnlyList<ControlAction> availableActionPrototypes => _prototypes;

	public BindingLinear() { }

	public BindingLinear(BindingLinear other) : base(other) { }

	public override float applyValueRaw(float wireValue)
		=> Math.Clamp(ContinuousFloatUtil.RoundToBindingDecimals(wireValue), minimum, maximum);

	public override float applyDeltaRaw(float currentWire, float wireDelta)
		=> applyValueRaw(currentWire + wireDelta);

	public override float getNormalizedRatio(float wire)
		=> (maximum > minimum) ? (wire - minimum) / (maximum - minimum) : 0f;
}
