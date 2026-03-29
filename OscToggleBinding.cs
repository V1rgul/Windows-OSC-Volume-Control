using System.ComponentModel;
using System.Windows.Forms;

namespace X32VolumeHijacker;

public sealed class OscToggleBinding {
	public string Name { get; set; } = "";
	public string Address { get; set; } = "";
	public Keys Hotkey { get; set; } = Keys.None;

	public OscToggleBinding() { }

	public OscToggleBinding(OscToggleBinding other) {
		ArgumentNullException.ThrowIfNull(other);
		Name = other.Name;
		Address = other.Address;
		Hotkey = other.Hotkey;
	}

	public static OscToggleBinding CreateDefaultMasterMute() => new() {
		Name = "Master mute",
		Address = "/main/st/mix/on",
		Hotkey = Keys.VolumeMute,
	};
}

static class OscHotkey {
	static readonly KeysConverter Converter = new();
	const Keys SupportedModifiers = Keys.Control | Keys.Shift | Keys.Alt;

	public static Keys Normalize(Keys hotkey) {
		Keys keyCode = hotkey & Keys.KeyCode;
		Keys modifiers = hotkey & SupportedModifiers;
		return keyCode | modifiers;
	}

	public static bool IsModifierKey(Keys key) => (key & Keys.KeyCode) switch {
		Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => true,
		Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => true,
		Keys.Menu or Keys.LMenu or Keys.RMenu => true,
		_ => false,
	};

	public static bool TryParse(string? text, out Keys hotkey) {
		hotkey = Keys.None;
		text = text?.Trim();
		if (string.IsNullOrEmpty(text))
			return false;
		try {
			object? value = Converter.ConvertFromInvariantString(text);
			if (value is not Keys keys)
				return false;
			hotkey = Normalize(keys);
			return hotkey != Keys.None && !IsModifierKey(hotkey);
		} catch (NotSupportedException) {
			return false;
		}
	}

	public static string Format(Keys hotkey) {
		hotkey = Normalize(hotkey);
		if (hotkey == Keys.None)
			return "";
		return Converter.ConvertToInvariantString(hotkey) ?? "";
	}

	public static bool TryValidate(Keys hotkey, out string error) {
		hotkey = Normalize(hotkey);
		Keys keyCode = hotkey & Keys.KeyCode;
		if (keyCode == Keys.None) {
			error = "Hotkey is required.";
			return false;
		}
		if (IsModifierKey(keyCode)) {
			error = "Hotkey must include a non-modifier key.";
			return false;
		}
		error = "";
		return true;
	}
}
