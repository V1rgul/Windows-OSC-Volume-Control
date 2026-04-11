using System.ComponentModel;
using Key = System.Windows.Input.Key;
using KeyConverter = System.Windows.Input.KeyConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyInterop = System.Windows.Input.KeyInterop;
using Keyboard = System.Windows.Input.Keyboard;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace WindowsOscVolumeControl;

[Flags]
public enum HotkeyModifiers {
	NONE = 0,
	CONTROL = 1 << 0,
	SHIFT = 1 << 1,
	ALT = 1 << 2,
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
	static readonly KeyConverter CONVERTER = new();

	public static HotkeyGesture normalize(HotkeyGesture hotkey) {
		if (hotkey.keyCode == 0)
			return HotkeyGesture.None;

		Key key = KeyInterop.KeyFromVirtualKey(hotkey.keyCode);
		if (isModifierKey(key))
			return HotkeyGesture.None;

		return new HotkeyGesture {
			keyCode = hotkey.keyCode,
			modifiers = hotkey.modifiers & (HotkeyModifiers.CONTROL | HotkeyModifiers.SHIFT | HotkeyModifiers.ALT),
		};
	}

	public static bool isModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.System
		or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt;

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
			switch (part.ToLowerInvariant()) {
				case "ctrl":
				case "control":
					modifiers |= HotkeyModifiers.CONTROL;
					continue;
				case "shift":
					modifiers |= HotkeyModifiers.SHIFT;
					continue;
				case "alt":
				case "menu":
					modifiers |= HotkeyModifiers.ALT;
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

		var parts = new List<string>(4);
		if ((hotkey.modifiers & HotkeyModifiers.CONTROL) != 0)
			parts.Add("Ctrl");
		if ((hotkey.modifiers & HotkeyModifiers.SHIFT) != 0)
			parts.Add("Shift");
		if ((hotkey.modifiers & HotkeyModifiers.ALT) != 0)
			parts.Add("Alt");

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
		if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
			modifiers |= HotkeyModifiers.CONTROL;
		if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
			modifiers |= HotkeyModifiers.SHIFT;
		if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
			modifiers |= HotkeyModifiers.ALT;

		return normalize(new HotkeyGesture {
			keyCode = KeyInterop.VirtualKeyFromKey(key),
			modifiers = modifiers,
		});
	}
}
