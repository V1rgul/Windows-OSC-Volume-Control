using System.Globalization;
using Result;

namespace WindowsOscVolumeControl.Config;

public readonly record struct ConfigFloatParseValue(float value, int fractionalDigits);

internal static class ConfigParseUtil {
	public static Result<string> parseRequiredText(string? text) {
		string trimmed = (text ?? "").Trim();
		if (trimmed.Length == 0)
			return new ResultError.Generic.Parsing { message = "Required." };
		if (containsControlCharacter(trimmed))
			return new ResultError.Generic.Parsing { message = "Must not contain control characters." };
		return trimmed;
	}

	public static Result<string?> parseOptionalText(string? text) {
		string trimmed = (text ?? "").Trim();
		if (trimmed.Length == 0)
			return (string?)null;
		if (containsControlCharacter(trimmed))
			return new ResultError.Generic.Parsing { message = "Must not contain control characters." };
		return trimmed;
	}

	public static Result<ConfigFloatParseValue> parseFiniteFloatWithDigits(string? text) {
		string trimmed = (text ?? "").Trim();
		if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed))
			return new ResultError.Generic.Parsing { message = "Must be a finite number." };
		return new ConfigFloatParseValue(parsed, ContinuousFloatUtil.fractionalDigitsOfTypedString(trimmed));
	}

	static bool containsControlCharacter(string text) {
		foreach (char c in text) {
			if (char.IsControl(c))
				return true;
		}
		return false;
	}
}
