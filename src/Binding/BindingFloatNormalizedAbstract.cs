namespace WindowsOscVolumeControl.Binding;

public abstract class BindingFloatNormalizedAbstract : BindingFloatAbstract {
	protected BindingFloatNormalizedAbstract(BindingFloatNormalizedAbstract other) : base(other) {
		rangeMinimum = ContinuousFloatUtil.RoundToBindingDecimals(other.rangeMinimum);
		rangeMaximum = ContinuousFloatUtil.RoundToBindingDecimals(other.rangeMaximum);
	}

	protected BindingFloatNormalizedAbstract() { }

	/// <summary>X32 curve domain (wire 0..1 maps linearly or logarithmically between these reals).</summary>
	public float rangeMinimum { get; set; }

	/// <summary>X32 curve domain upper bound.</summary>
	public float rangeMaximum { get; set; } = 1f;

	public abstract float toReal(float wire);
	public abstract float toWire(float real);

	public virtual float clampReal(float real) => Math.Clamp(real, minimum, maximum);

	public override float applyValueRaw(float wireValue) {
		float a = toWire(minimum);
		float b = toWire(maximum);
		float lo = Math.Min(a, b);
		float hi = Math.Max(a, b);
		return Math.Clamp(wireValue, lo, hi);
	}

	public override float applyDeltaRaw(float currentWire, float wireDelta)
		=> applyValueRaw(currentWire + wireDelta);

	public override float getNormalizedRatio(float wire) {
		float a = toWire(minimum);
		float b = toWire(maximum);
		float lo = Math.Min(a, b);
		float hi = Math.Max(a, b);
		float span = hi - lo;
		if (span < 1e-30f)
			return 0f;
		return Math.Clamp((wire - lo) / span, 0f, 1f);
	}

	public float applyValueReal(float realValue)
		=> toWire(clampReal(realValue));

	public float applyDeltaReal(float currentWire, float realDelta)
		=> toWire(clampReal(toReal(currentWire) + realDelta));

	public override float applyContinuousAction(ControlActionContinuousAbstract action, float currentWire)
		=> action switch {
			ControlActionContinuousSet s => applyValueReal(s.value),
			ControlActionContinuousDelta d => applyDeltaReal(currentWire, d.delta),
			ControlActionContinuousRawDelta r => applyDeltaRaw(currentWire, r.delta),
			_ => base.applyContinuousAction(action, currentWire),
		};

	protected override int digitsForRawDelta(float d) {
		float ad = Math.Abs(d);
		if (ad <= 0f || !float.IsFinite(ad))
			return 0;
		double smallest = double.PositiveInfinity;
		for (int i = 0; i <= 10; i++) {
			float w = i / 10f;
			float w2 = Math.Clamp(w + ad, 0f, 1f);
			if (w2 == w)
				continue;
			float r0 = toReal(w);
			float r1 = toReal(w2);
			if (!float.IsFinite(r0) || !float.IsFinite(r1))
				continue;
			double dr = Math.Abs(r1 - r0);
			if (dr > 0 && dr < smallest)
				smallest = dr;
		}
		return double.IsPositiveInfinity(smallest)
			? 0
			: ContinuousFloatUtil.fractionalDigitsForMagnitude(smallest);
	}
}
