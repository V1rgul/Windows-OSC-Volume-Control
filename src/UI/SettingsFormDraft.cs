using System.Globalization;
using System.Linq;
using System.Net;

namespace WindowsOscVolumeControl;

/// <summary>Builds <see cref="AppConfig"/> from settings-window field values; owns validation copy as <see cref="UiTextFeedback"/>.</summary>
static class SettingsFormDraft {
	public static (bool ok, AppConfig? config, UiTextFeedback? error) tryBuild(
		string ipText,
		string portText,
		string timeoutText,
		string cacheTtlText,
		OSDController.Config.OsdScreenAnchor osdScreenAnchor,
		string osdHeightText,
		string osdDurationText,
		string hotkeyLongPressMsText,
		bool hotkeyOptimizeNonLongPressKeyDown,
		bool hotkeySuppressKeyForLongPressOnlyGestures,
		bool hotkeyAcceptMacroChordKeyOrder,
		IReadOnlyList<BindingEditor> bindings) {
		if (!OscConnectionConfigParse.tryParseIpPort(ipText, portText, out IPAddress ip, out int port, out _, out string? oscError))
			return (false, null, new UiTextFeedback(oscError ?? "Invalid OSC IP/port.", UiTextFeedbackKind.ERROR));

		if (!tryParseUInt(timeoutText, MixerController.Config.MIN_TIMEOUT_MS, MixerController.Config.MAX_TIMEOUT_MS, "Query timeout", out uint timeout, out string? e1))
			return (false, null, feedback(e1));
		if (!tryParseUInt(cacheTtlText, 0, MixerController.Config.MAX_VALUE_CACHE_TTL_MS, "Value cache TTL", out uint ttl, out string? e2))
			return (false, null, feedback(e2));
		if (!tryParseInt(osdHeightText, OSDController.Config.MIN_HEIGHT_DIP, OSDController.Config.MAX_HEIGHT_DIP, "OSD height", out int osdHeight, out string? e3))
			return (false, null, feedback(e3));
		if (!tryParseUInt(osdDurationText, OSDController.Config.MIN_DISPLAY_DURATION_MS, OSDController.Config.MAX_DISPLAY_DURATION_MS, "OSD display duration", out uint osdDuration, out string? e4))
			return (false, null, feedback(e4));
		if (!tryParseUInt(hotkeyLongPressMsText, KeyboardHook.Config.MIN_LONG_PRESS_MS, KeyboardHook.Config.MAX_LONG_PRESS_MS, "Long-press duration", out uint hotkeyLongPressMs, out string? e5))
			return (false, null, feedback(e5));

		var built = new List<BindingAbstract>();
		for (int i = 0; i < bindings.Count; i++) {
			BindingEditor editor = bindings[i];
			if (editor.isDeleted || isBindingBlank(editor))
				continue;

			if (string.IsNullOrWhiteSpace(editor.name) || string.IsNullOrWhiteSpace(editor.address))
				return (false, null, new UiTextFeedback($"Binding {i + 1} requires name and OSC address.", UiTextFeedbackKind.ERROR));

			if (editor.type == BindingEditorType.FADER) {
				if (!tryParseFloat(editor.minimum, "Minimum", out float min, out string? emin))
					return (false, null, feedback(emin));
				if (!tryParseFloat(editor.maximum, "Maximum", out float max, out string? emax))
					return (false, null, feedback(emax));
				if (min > max)
					return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
				min = FaderFloatUtil.RoundToBindingDecimals(min);
				max = FaderFloatUtil.RoundToBindingDecimals(max);
				var fader = new BindingFader {
					name = editor.name.Trim(),
					address = editor.address.Trim(),
					minimum = min,
					maximum = max,
				};
				for (int h = 0; h < editor.hotkeys.Count; h++) {
					HotkeyActionEditor hk = editor.hotkeys[h];
					if (hk.isDeleted || isHotkeyRowBlank(hk))
						continue;
					if (!hk.tryBuildModel(editor.type, out HotkeyAction? action, out string? hkErr))
						return (false, null, new UiTextFeedback($"Binding {i + 1}, hotkey {h + 1}: {hkErr}", UiTextFeedbackKind.ERROR));
					fader.hotkeys.Add(action!);
				}
				built.Add(fader);
			} else {
				var toggle = new BindingToggle {
					name = editor.name.Trim(),
					address = editor.address.Trim(),
				};
				for (int h = 0; h < editor.hotkeys.Count; h++) {
					HotkeyActionEditor hk = editor.hotkeys[h];
					if (hk.isDeleted || isHotkeyRowBlank(hk))
						continue;
					if (!hk.tryBuildModel(editor.type, out HotkeyAction? action, out string? hkErr))
						return (false, null, new UiTextFeedback($"Binding {i + 1}, hotkey {h + 1}: {hkErr}", UiTextFeedbackKind.ERROR));
					toggle.hotkeys.Add(action!);
				}
				built.Add(toggle);
			}
		}

		if (built.OfType<BindingFader>().FirstOrDefault() == null)
			return (false, null, new UiTextFeedback("Add at least one non-deleted fader binding with name and address.", UiTextFeedbackKind.ERROR));

		var cfg = new AppConfig {
			oscTransport = new OscTransport.Config {
				endPoint = new IPEndPoint(ip, port),
			},
			mixer = new MixerController.Config {
				timeoutMs = timeout,
				ValueCacheTtlMs = ttl,
			},
			osd = new OSDController.Config {
				heightDip = osdHeight,
				DisplayDurationMs = osdDuration,
				screenAnchor = OSDController.Config.clampScreenAnchor(osdScreenAnchor),
			},
			trayApp = new BindingManager.Config {
				bindings = built,
			},
			keyboardHook = KeyboardHook.Config.Clamped(new KeyboardHook.Config {
				longPressDurationMs = hotkeyLongPressMs,
				optimizeNonLongPressKeyDown = hotkeyOptimizeNonLongPressKeyDown,
				suppressKeyForLongPressOnlyGestures = hotkeySuppressKeyForLongPressOnlyGestures,
				acceptMacroChordKeyOrder = hotkeyAcceptMacroChordKeyOrder,
			}),
		};
		return (true, cfg, null);
	}

	static UiTextFeedback feedback(string? message) =>
		new(message ?? "Invalid configuration.", UiTextFeedbackKind.ERROR);

	static bool tryParseUInt(string text, uint min, uint max, string label, out uint value, out string? error) {
		value = 0;
		error = null;
		if (!uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed)) {
			error = $"{label} must be an integer.";
			return false;
		}
		if (parsed < min || parsed > max) {
			error = $"{label} must be between {min} and {max}.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool tryParseInt(string text, int min, int max, string label, out int value, out string? error) {
		value = 0;
		error = null;
		if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
			error = $"{label} must be an integer.";
			return false;
		}
		if (parsed < min || parsed > max) {
			error = $"{label} must be between {min} and {max}.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool tryParseFloat(string text, string label, out float value, out string? error) {
		value = 0;
		error = null;
		if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed)) {
			error = $"{label} must be a finite number.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool isBindingBlank(BindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& string.IsNullOrWhiteSpace(editor.minimum)
		&& string.IsNullOrWhiteSpace(editor.maximum)
		&& editor.hotkeys.Count == 0;

	static bool isHotkeyRowBlank(HotkeyActionEditor hk) => hk.hotkey.isNone;
}
