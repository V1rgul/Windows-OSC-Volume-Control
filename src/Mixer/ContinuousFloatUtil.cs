using System.Globalization;

namespace WindowsOscVolumeControl;

/// <summary>
/// Rounds continuous binding min/max/values for UI and config. Grid decimals follow
/// <see cref="MixerController.Config.MIN_CONTINUOUS_STEP"/> (e.g. 0.001 → 3 places).
/// </summary>
public static class ContinuousFloatUtil {
	const double SCALE_INTEGER_EPSILON = 1e-7;

	/// <summary>Fractional digits implied by the minimum step (same as grid / binding rounding).</summary>
	public static int BindingFractionalDigits => GetFractionalDigitsForMinStep(MixerController.Config.MIN_CONTINUOUS_STEP);

	/// <summary>Smallest N such that <paramref name="minStep"/>·10^N is (within ε) an integer.</summary>
	public static int GetFractionalDigitsForMinStep(float minStep) {
		double s = minStep;
		if (!double.IsFinite(s) || s <= 0)
			return 0;
		for (int n = 0; n <= 14; n++) {
			double scaled = s * Math.Pow(10.0, n);
			if (Math.Abs(scaled - Math.Round(scaled)) < SCALE_INTEGER_EPSILON)
				return n;
		}
		return 14;
	}

	public static float RoundToBindingDecimals(float value) {
		int n = BindingFractionalDigits;
		return n <= 0
			? (float)Math.Round(value, MidpointRounding.AwayFromZero)
			: (float)Math.Round(value, n, MidpointRounding.AwayFromZero);
	}

	public static string FormatGridFloat(float value) {
		int n = BindingFractionalDigits;
		float r = RoundToBindingDecimals(value);
		return n <= 0
			? ((int)Math.Round(r, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)
			: r.ToString("F" + n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}

	/// <summary>Count fractional digits from the user-typed string (e.g. <c>0.250</c> → 3).</summary>
	public static int fractionalDigitsOfTypedString(string? text) {
		if (string.IsNullOrWhiteSpace(text))
			return 0;
		string s = text.Trim();
		int dot = s.IndexOf('.');
		if (dot < 0)
			return 0;
		int end = s.Length;
		while (end > dot + 1 && s[end - 1] == '0')
			end--;
		return Math.Max(0, end - dot - 1);
	}

	/// <summary>Digit count so a magnitude <paramref name="m"/> is visible (OSD raw-delta heuristic).</summary>
	public static int fractionalDigitsForMagnitude(double m) {
		if (!double.IsFinite(m) || m <= 0)
			return 0;
		return Math.Clamp((int)Math.Ceiling(-Math.Log10(m)), 0, 6);
	}

	/// <summary>Smallest N such that <paramref name="value"/>·10^N is (within ε) an integer (for stable UI formatting).</summary>
	public static int fractionalDigitsForValue(float value, int maxDigits = 6) {
		double v = value;
		if (!double.IsFinite(v))
			return 0;
		maxDigits = Math.Clamp(maxDigits, 0, 14);
		for (int n = 0; n <= maxDigits; n++) {
			double scaled = v * Math.Pow(10.0, n);
			if (Math.Abs(scaled - Math.Round(scaled)) < SCALE_INTEGER_EPSILON)
				return n;
		}
		return maxDigits;
	}

	/// <summary>
	/// Fractional digit count implied by <paramref name="step"/> after binding rounding
	/// (e.g. 0.02 → 2), capped by <see cref="BindingFractionalDigits"/>.
	/// </summary>
	public static int GetOsdFractionalDigitsFromStep(float step) {
		float q = RoundToBindingDecimals(step);
		if (!float.IsFinite(q) || q <= 0f)
			return BindingFractionalDigits;
		int max = BindingFractionalDigits;
		string pattern = max <= 0 ? "0" : "0." + new string('#', max);
		string s = q.ToString(pattern, CultureInfo.InvariantCulture);
		int dot = s.IndexOf('.');
		if (dot < 0)
			return 0;
		return Math.Min(max, s.Length - dot - 1);
	}

	public static float RoundForOsdDisplay(float value, int fractionalDigits) {
		fractionalDigits = Math.Clamp(fractionalDigits, 0, Math.Max(0, BindingFractionalDigits));
		return (float)Math.Round(value, fractionalDigits, MidpointRounding.AwayFromZero);
	}

	public static string FormatOsdLevelValue(float rawValue, int fractionalDigits) {
		int cap = Math.Max(0, BindingFractionalDigits);
		fractionalDigits = Math.Clamp(fractionalDigits, 0, cap);
		float v = RoundForOsdDisplay(rawValue, fractionalDigits);
		return fractionalDigits == 0
			? ((int)Math.Round(v, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)
			: v.ToString("F" + fractionalDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}

	/// <summary>Representative OSD value string for layout reserve.</summary>
	public static string OsdMeasureSample(int fractionalDigits) {
		int cap = Math.Max(0, BindingFractionalDigits);
		fractionalDigits = Math.Clamp(fractionalDigits, 0, cap);
		return FormatOsdLevelValue(123.4567f, fractionalDigits);
	}

	public static string formatFloatForConfig(float value, int fractionalDigits) {
		fractionalDigits = Math.Clamp(fractionalDigits, 0, 14);
		return value.ToString("F" + fractionalDigits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
	}
}
