namespace WindowsOscVolumeControl.Binding;

public sealed class BindingLinf : BindingFloatNormalizedAbstract {
	static readonly ControlAction[] _prototypes = [
		new ControlActionContinuousSet(),
		new ControlActionContinuousDelta(),
		new ControlActionContinuousRawDelta(),
	];

	public override IReadOnlyList<ControlAction> availableActionPrototypes => _prototypes;

	public BindingLinf() { }

	public BindingLinf(BindingLinf other) : base(other) { }

	public override float toReal(float wire) {
		float w = Math.Clamp(wire, 0f, 1f);
		return rangeMinimum + w * (rangeMaximum - rangeMinimum);
	}

	public override float toWire(float real) {
		float span = rangeMaximum - rangeMinimum;
		if (Math.Abs(span) < 1e-30f)
			return 0f;
		return Math.Clamp((real - rangeMinimum) / span, 0f, 1f);
	}
}
