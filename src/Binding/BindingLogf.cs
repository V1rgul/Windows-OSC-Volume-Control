namespace WindowsOscVolumeControl;

public sealed class BindingLogf : BindingFloatNormalizedAbstract {
	static readonly ControlAction[] _prototypes = [
		new ControlActionContinuousSet(),
		new ControlActionContinuousDelta(),
		new ControlActionContinuousRawDelta(),
	];

	public override IReadOnlyList<ControlAction> availableActionPrototypes => _prototypes;

	public BindingLogf() { }

	public BindingLogf(BindingLogf other) : base(other) { }

	public override float toReal(float wire) {
		float w = Math.Clamp(wire, 0f, 1f);
		double lo = minimum;
		double hi = maximum;
		if (!double.IsFinite(lo) || !double.IsFinite(hi) || lo <= 0 || hi <= 0 || hi < lo)
			return (float)lo;
		double ratio = hi / lo;
		return (float)(lo * Math.Pow(ratio, w));
	}

	public override float toWire(float real) {
		double lo = minimum;
		double hi = maximum;
		if (!double.IsFinite(lo) || !double.IsFinite(hi) || lo <= 0 || hi <= 0 || hi < lo)
			return 0f;
		double r = real;
		r = Math.Clamp(r, lo, hi);
		double ratio = hi / lo;
		double w = Math.Log(r / lo) / Math.Log(ratio);
		return (float)Math.Clamp(w, 0.0, 1.0);
	}
}
