namespace WindowsOscVolumeControl;

public abstract class BindingFloatNormalizedAbstract : BindingFloatAbstract {
	protected BindingFloatNormalizedAbstract(BindingFloatNormalizedAbstract other) : base(other) { }

	protected BindingFloatNormalizedAbstract() { }

	public abstract float toReal(float wire);
	public abstract float toWire(float real);

	public virtual float clampReal(float real) => Math.Clamp(real, minimum, maximum);

	public override float applyValueRaw(float wireValue)
		=> Math.Clamp(wireValue, 0f, 1f);

	public override float applyDeltaRaw(float currentWire, float wireDelta)
		=> Math.Clamp(currentWire + wireDelta, 0f, 1f);

	public override float getNormalizedRatio(float wire) => wire;

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
