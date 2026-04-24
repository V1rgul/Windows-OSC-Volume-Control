using Key = System.Windows.Input.Key;
using KeyConverter = System.Windows.Input.KeyConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyInterop = System.Windows.Input.KeyInterop;
using Keyboard = System.Windows.Input.Keyboard;

namespace WindowsOscVolumeControl;

[Flags]
public enum HotkeyModifiers {
	NONE = 0,
	LEFT_CONTROL = 1 << 0,
	RIGHT_CONTROL = 1 << 1,
	LEFT_SHIFT = 1 << 2,
	RIGHT_SHIFT = 1 << 3,
	LEFT_ALT = 1 << 4,
	RIGHT_ALT = 1 << 5,
}

public readonly record struct HotkeyGesture {
	public const int VK_VOLUME_MUTE = 0xAD;
	public const int VK_VOLUME_DOWN = 0xAE;
	public const int VK_VOLUME_UP = 0xAF;

	public int keyCode { get; init; }
	public HotkeyModifiers modifiers { get; init; }

	public static HotkeyGesture None => new() { keyCode = 0, modifiers = HotkeyModifiers.NONE };

	public bool isNone => keyCode == 0;
}

public static class HotkeyUtil {
	public const HotkeyModifiers CTRL_FAMILY = HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.RIGHT_CONTROL;
	public const HotkeyModifiers SHIFT_FAMILY = HotkeyModifiers.LEFT_SHIFT | HotkeyModifiers.RIGHT_SHIFT;
	public const HotkeyModifiers ALT_FAMILY = HotkeyModifiers.LEFT_ALT | HotkeyModifiers.RIGHT_ALT;
	public const HotkeyModifiers ALL_SIDE_MODIFIERS = CTRL_FAMILY | SHIFT_FAMILY | ALT_FAMILY;

	static readonly KeyConverter CONVERTER = new();

	/// <summary>True when <paramref name="activeSides"/> satisfies <paramref name="required"/> (subset for held keys, and no extra keys in families the gesture does not use).</summary>
	public static bool activeSidesMatchGesture(HotkeyModifiers required, HotkeyModifiers activeSides) {
		if ((activeSides & required) != required)
			return false;
		if ((required & CTRL_FAMILY) == 0 && (activeSides & CTRL_FAMILY) != 0)
			return false;
		if ((required & SHIFT_FAMILY) == 0 && (activeSides & SHIFT_FAMILY) != 0)
			return false;
		if ((required & ALT_FAMILY) == 0 && (activeSides & ALT_FAMILY) != 0)
			return false;
		return true;
	}

	public static HotkeyGesture normalize(HotkeyGesture hotkey) {
		if (hotkey.keyCode == 0)
			return HotkeyGesture.None;

		Key key = KeyInterop.KeyFromVirtualKey(hotkey.keyCode);
		if (isModifierKey(key))
			return HotkeyGesture.None;

		return new HotkeyGesture {
			keyCode = hotkey.keyCode,
			modifiers = hotkey.modifiers & ALL_SIDE_MODIFIERS,
		};
	}

	public static bool isModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.System
		or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt;

	static bool tryParseModifierToken(string lowered, out HotkeyModifiers mod) {
		switch (lowered) {
			case "leftctrl":
			case "lctrl":
				mod = HotkeyModifiers.LEFT_CONTROL;
				return true;
			case "rightctrl":
			case "rctrl":
				mod = HotkeyModifiers.RIGHT_CONTROL;
				return true;
			case "leftshift":
			case "lshift":
				mod = HotkeyModifiers.LEFT_SHIFT;
				return true;
			case "rightshift":
			case "rshift":
				mod = HotkeyModifiers.RIGHT_SHIFT;
				return true;
			case "leftalt":
			case "lalt":
				mod = HotkeyModifiers.LEFT_ALT;
				return true;
			case "rightalt":
			case "ralt":
				mod = HotkeyModifiers.RIGHT_ALT;
				return true;
			default:
				mod = HotkeyModifiers.NONE;
				return false;
		}
	}

	public static bool tryParse(string? text, out HotkeyGesture hotkey) {
		hotkey = HotkeyGesture.None;
		text = text?.Trim();
		if (string.IsNullOrEmpty(text))
			return false;

		string normalizedText = text.Replace(',', '+');
		string[] parts = normalizedText
			.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
			return false;

		HotkeyModifiers modifiers = HotkeyModifiers.NONE;
		Key? key = null;
		foreach (string part in parts) {
			string lowered = part.ToLowerInvariant();
			if (tryParseModifierToken(lowered, out HotkeyModifiers mod)) {
				modifiers |= mod;
				continue;
			}

			if (key != null)
				return false;

			if (!tryParseKey(part, out Key parsedKey))
				return false;
			key = parsedKey;
		}

		if (key == null || isModifierKey(key.Value))
			return false;

		hotkey = normalize(new HotkeyGesture {
			keyCode = KeyInterop.VirtualKeyFromKey(key.Value),
			modifiers = modifiers,
		});
		return !hotkey.isNone;
	}

	static bool tryParseKey(string text, out Key key) {
		try {
			object? converted = CONVERTER.ConvertFromInvariantString(text);
			if (converted is Key parsedKey && parsedKey != Key.None) {
				key = parsedKey;
				return true;
			}
		} catch (NotSupportedException) {
		}

		switch (text.ToLowerInvariant()) {
			case "volumemute":
				key = Key.VolumeMute;
				return true;
			case "volumedown":
				key = Key.VolumeDown;
				return true;
			case "volumeup":
				key = Key.VolumeUp;
				return true;
			default:
				key = Key.None;
				return false;
		}
	}

	public static string format(HotkeyGesture hotkey) {
		hotkey = normalize(hotkey);
		if (hotkey.isNone)
			return "";

		var parts = new List<string>(8);
		if ((hotkey.modifiers & HotkeyModifiers.LEFT_CONTROL) != 0)
			parts.Add("LeftCtrl");
		if ((hotkey.modifiers & HotkeyModifiers.RIGHT_CONTROL) != 0)
			parts.Add("RightCtrl");
		if ((hotkey.modifiers & HotkeyModifiers.LEFT_SHIFT) != 0)
			parts.Add("LeftShift");
		if ((hotkey.modifiers & HotkeyModifiers.RIGHT_SHIFT) != 0)
			parts.Add("RightShift");
		if ((hotkey.modifiers & HotkeyModifiers.LEFT_ALT) != 0)
			parts.Add("LeftAlt");
		if ((hotkey.modifiers & HotkeyModifiers.RIGHT_ALT) != 0)
			parts.Add("RightAlt");

		Key key = KeyInterop.KeyFromVirtualKey(hotkey.keyCode);
		string keyText = CONVERTER.ConvertToInvariantString(key) ?? key.ToString();
		parts.Add(keyText);
		return string.Join("+", parts);
	}

	public static bool tryValidate(HotkeyGesture hotkey, out string error) {
		bool ok = tryValidate(hotkey, out UiTextFeedback fb);
		error = ok ? "" : fb.text;
		return ok;
	}

	public static bool tryValidate(HotkeyGesture hotkey, out UiTextFeedback feedback) {
		hotkey = normalize(hotkey);
		if (hotkey.isNone) {
			feedback = new UiTextFeedback("Hotkey is required.", UiTextFeedbackKind.ERROR);
			return false;
		}

		Key key = KeyInterop.KeyFromVirtualKey(hotkey.keyCode);
		if (isModifierKey(key)) {
			feedback = new UiTextFeedback("Hotkey must include a non-modifier key.", UiTextFeedbackKind.ERROR);
			return false;
		}

		feedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		return true;
	}

	public static HotkeyGesture fromKeyEventArgs(KeyEventArgs e) {
		Key key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (key == Key.None)
			return HotkeyGesture.None;

		HotkeyModifiers modifiers = HotkeyModifiers.NONE;
		if (Keyboard.IsKeyDown(Key.LeftCtrl))
			modifiers |= HotkeyModifiers.LEFT_CONTROL;
		if (Keyboard.IsKeyDown(Key.RightCtrl))
			modifiers |= HotkeyModifiers.RIGHT_CONTROL;
		if (Keyboard.IsKeyDown(Key.LeftShift))
			modifiers |= HotkeyModifiers.LEFT_SHIFT;
		if (Keyboard.IsKeyDown(Key.RightShift))
			modifiers |= HotkeyModifiers.RIGHT_SHIFT;
		if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.System))
			modifiers |= HotkeyModifiers.LEFT_ALT;
		if (Keyboard.IsKeyDown(Key.RightAlt))
			modifiers |= HotkeyModifiers.RIGHT_ALT;

		return normalize(new HotkeyGesture {
			keyCode = KeyInterop.VirtualKeyFromKey(key),
			modifiers = modifiers,
		});
	}
}
