namespace WindowsOscVolumeControl;

public abstract class BindingFloatAbstract : BindingAbstract {
	protected BindingFloatAbstract(BindingFloatAbstract other) : base(other) {
		ArgumentNullException.ThrowIfNull(other);
		minimum = ContinuousFloatUtil.RoundToBindingDecimals(other.minimum);
		maximum = ContinuousFloatUtil.RoundToBindingDecimals(other.maximum);
		minimumFractionalDigits = other.minimumFractionalDigits;
		maximumFractionalDigits = other.maximumFractionalDigits;
		unit = other.unit;
	}

	public float minimum { get; set; }
	public int minimumFractionalDigits { get; set; }
	public float maximum { get; set; }
	public int maximumFractionalDigits { get; set; }

	/// <summary>Display unit suffix. Null = dimensionless / pure ratio (no suffix in OSD).</summary>
	public virtual string? unit { get; set; }

	protected BindingFloatAbstract() { }

	public abstract float applyValueRaw(float wireValue);
	public abstract float applyDeltaRaw(float currentWire, float wireDelta);
	public abstract float getNormalizedRatio(float wire);

	public virtual float applyContinuousAction(ControlActionContinuousAbstract action, float currentWire)
		=> action switch {
			ControlActionContinuousSet s => applyValueRaw(s.value),
			ControlActionContinuousDelta d => applyDeltaRaw(currentWire, d.delta),
			ControlActionContinuousRawDelta r => applyDeltaRaw(currentWire, r.delta),
			_ => throw new NotSupportedException(action.GetType().Name),
		};

	public virtual int osdFractionalDigits {
		get {
			int max = 0;
			max = Math.Max(max, minimumFractionalDigits);
			max = Math.Max(max, maximumFractionalDigits);
			foreach (ControlAction a in actions) {
				if (a is not ControlActionContinuousAbstract ca)
					continue;
				int d = ca switch {
					ControlActionContinuousSet s => s.fractionalDigits,
					ControlActionContinuousDelta dl => dl.fractionalDigits,
					ControlActionContinuousRawDelta r => digitsForRawDelta(r.delta),
					_ => 0,
				};
				max = Math.Max(max, d);
			}
			return Math.Clamp(max, 0, 6);
		}
	}

	protected virtual int digitsForRawDelta(float d)
		=> ContinuousFloatUtil.fractionalDigitsForMagnitude(Math.Abs((double)d));
}
