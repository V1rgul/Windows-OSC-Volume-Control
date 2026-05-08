namespace WindowsOscVolumeControl.Binding;

/// <summary>X32 <c>level</c> curve: piecewise linear dB vs normalized wire; <c>wire == 0</c> is mute (−∞ dB).</summary>
public sealed class BindingLevel : BindingFloatNormalizedAbstract {
	static readonly ControlAction[] _prototypes = [
		new ControlActionContinuousSet(),
		new ControlActionContinuousDelta(),
		new ControlActionContinuousRawDelta(),
	];

	const float w1 = 0.0625f;
	const float w2 = 0.25f;
	const float w3 = 0.5f;

	public override IReadOnlyList<ControlAction> availableActionPrototypes => _prototypes;

	public override string? unit {
		get => "dB";
		set { /* fixed; ignore writes for config round-trip */ }
	}

	public BindingLevel() { }

	public BindingLevel(BindingLevel other) : base(other) { }

	public override float clampReal(float real) {
		if (float.IsNegativeInfinity(real))
			return real;
		return Math.Clamp(real, minimum, maximum);
	}

	public override float toReal(float wire) {
		if (wire <= 0f)
			return float.NegativeInfinity;
		float w = Math.Clamp(wire, 0f, 1f);
		if (w < w1)
			return -90f + (w / w1) * 30f;
		if (w < w2)
			return -60f + ((w - w1) / (w2 - w1)) * 30f;
		if (w < w3)
			return -30f + ((w - w2) / (w3 - w2)) * 20f;
		return -10f + ((w - w3) / (1f - w3)) * 20f;
	}

	public override float toWire(float real) {
		if (float.IsNegativeInfinity(real))
			return 0f;
		float r = clampReal(real);
		if (r <= -90f)
			return 0f;
		if (r <= -60f)
			return (r + 90f) / 30f * w1;
		if (r <= -30f)
			return w1 + (r + 60f) / 30f * (w2 - w1);
		if (r <= -10f)
			return w2 + (r + 30f) / 20f * (w3 - w2);
		return w3 + (r + 10f) / 20f * (1f - w3);
	}

	public override float applyContinuousAction(ControlActionContinuousAbstract action, float currentWire) {
		// For level, wire==0 is mute (−∞ dB). Applying a positive delta should unmute from minimum.
		if (action is ControlActionContinuousDelta d && (currentWire <= 0f || !float.IsFinite(currentWire))) {
			if (!float.IsFinite(d.delta) || d.delta <= 0f)
				return 0f;
			return toWire(clampReal(minimum + d.delta));
		}
		return base.applyContinuousAction(action, currentWire);
	}
}
