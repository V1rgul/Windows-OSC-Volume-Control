using System.ComponentModel;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public sealed class OscToggleBinding {
	public string name { get; set; } = "";
	public string address { get; set; } = "";
	public Keys hotkey { get; set; } = Keys.None;

	public OscToggleBinding() { }

	public OscToggleBinding(OscToggleBinding other) {
		ArgumentNullException.ThrowIfNull(other);
		name = other.name;
		address = other.address;
		hotkey = other.hotkey;
	}

	public static OscToggleBinding createDefaultMasterMute() => new() {
		name = "MAIN",
		address = "/main/st/mix/on",
		hotkey = Keys.VolumeMute,
	};
}

static class OscHotkey {
	static readonly KeysConverter CONVERTER = new();
	const Keys SUPPORTED_MODIFIERS = Keys.Control | Keys.Shift | Keys.Alt;

	public static Keys normalize(Keys hotkey) {
		Keys keyCode = hotkey & Keys.KeyCode;
		Keys modifiers = hotkey & SUPPORTED_MODIFIERS;
		return keyCode | modifiers;
	}

	public static bool isModifierKey(Keys key) => (key & Keys.KeyCode) switch {
		Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => true,
		Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => true,
		Keys.Menu or Keys.LMenu or Keys.RMenu => true,
		_ => false,
	};

	public static bool tryParse(string? text, out Keys hotkey) {
		hotkey = Keys.None;
		text = text?.Trim();
		if (string.IsNullOrEmpty(text))
			return false;
		try {
			object? value = CONVERTER.ConvertFromInvariantString(text);
			if (value is not Keys keys)
				return false;
			hotkey = normalize(keys);
			return hotkey != Keys.None && !isModifierKey(hotkey);
		} catch (NotSupportedException) {
			return false;
		}
	}

	public static string format(Keys hotkey) {
		hotkey = normalize(hotkey);
		if (hotkey == Keys.None)
			return "";
		return CONVERTER.ConvertToInvariantString(hotkey) ?? "";
	}

	public static bool tryValidate(Keys hotkey, out string error) {
		hotkey = normalize(hotkey);
		Keys keyCode = hotkey & Keys.KeyCode;
		if (keyCode == Keys.None) {
			error = "Hotkey is required.";
			return false;
		}
		if (isModifierKey(keyCode)) {
			error = "Hotkey must include a non-modifier key.";
			return false;
		}
		error = "";
		return true;
	}
}
