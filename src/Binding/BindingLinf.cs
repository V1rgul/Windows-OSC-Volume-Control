namespace WindowsOscVolumeControl;

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
		return minimum + w * (maximum - minimum);
	}

	public override float toWire(float real) {
		float span = maximum - minimum;
		if (Math.Abs(span) < 1e-30f)
			return 0f;
		return Math.Clamp((real - minimum) / span, 0f, 1f);
	}
}
