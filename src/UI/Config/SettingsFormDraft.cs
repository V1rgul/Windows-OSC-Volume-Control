using System.Globalization;
using System.Linq;
using System.Net;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.UI.Config;

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

			switch (editor.type) {
				case BindingEditorType.LINEAR:
					if (!tryParseFloatWithDigits(editor.minimum, "Minimum", out float min, out int minDig, out string? emin))
						return (false, null, feedback(emin));
					if (!tryParseFloatWithDigits(editor.maximum, "Maximum", out float max, out int maxDig, out string? emax))
						return (false, null, feedback(emax));
					if (min > max)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					min = ContinuousFloatUtil.RoundToBindingDecimals(min);
					max = ContinuousFloatUtil.RoundToBindingDecimals(max);
					var linear = new BindingLinear {
						name = editor.name.Trim(),
						address = editor.address.Trim(),
						minimum = min,
						maximum = max,
						minimumFractionalDigits = minDig,
						maximumFractionalDigits = maxDig,
						unit = string.IsNullOrWhiteSpace(editor.unit) ? null : editor.unit.Trim(),
					};
					if (!appendActions(editor, i, linear.actions, out UiTextFeedback? hkErr))
						return (false, null, hkErr);
					built.Add(linear);
					break;
				case BindingEditorType.LINF:
					if (!tryParseFloatWithDigits(editor.minimum, "Minimum", out float lmin, out int lminDig, out string? elmin))
						return (false, null, feedback(elmin));
					if (!tryParseFloatWithDigits(editor.maximum, "Maximum", out float lmax, out int lmaxDig, out string? elmax))
						return (false, null, feedback(elmax));
					if (lmin > lmax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					if (!tryParseOptionalRange(editor.rangeMinimum, editor.rangeMaximum, lmin, lmax, i + 1, "linf", out float lrMin, out float lrMax, out string? rangeErr))
						return (false, null, feedback(rangeErr));
					if (lrMin > lrMax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: range min must be less than or equal to range max.", UiTextFeedbackKind.ERROR));
					var linf = new BindingLinf {
						name = editor.name.Trim(),
						address = editor.address.Trim(),
						minimum = ContinuousFloatUtil.RoundToBindingDecimals(lmin),
						maximum = ContinuousFloatUtil.RoundToBindingDecimals(lmax),
						rangeMinimum = ContinuousFloatUtil.RoundToBindingDecimals(lrMin),
						rangeMaximum = ContinuousFloatUtil.RoundToBindingDecimals(lrMax),
						minimumFractionalDigits = lminDig,
						maximumFractionalDigits = lmaxDig,
						unit = string.IsNullOrWhiteSpace(editor.unit) ? null : editor.unit.Trim(),
					};
					if (!appendActions(editor, i, linf.actions, out UiTextFeedback? hkErr2))
						return (false, null, hkErr2);
					built.Add(linf);
					break;
				case BindingEditorType.LOGF:
					if (!tryParseFloatWithDigits(editor.minimum, "Minimum", out float gmin, out int gminDig, out string? egmin))
						return (false, null, feedback(egmin));
					if (!tryParseFloatWithDigits(editor.maximum, "Maximum", out float gmax, out int gmaxDig, out string? egmax))
						return (false, null, feedback(egmax));
					if (gmin > gmax || gmin <= 0f || gmax <= 0f)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: logf requires positive minimum and maximum.", UiTextFeedbackKind.ERROR));
					if (!tryParseOptionalRange(editor.rangeMinimum, editor.rangeMaximum, gmin, gmax, i + 1, "logf", out float grMin, out float grMax, out string? gRangeErr))
						return (false, null, feedback(gRangeErr));
					if (grMin > grMax || grMin <= 0f || grMax <= 0f)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: logf range requires positive range min and range max.", UiTextFeedbackKind.ERROR));
					var logf = new BindingLogf {
						name = editor.name.Trim(),
						address = editor.address.Trim(),
						minimum = ContinuousFloatUtil.RoundToBindingDecimals(gmin),
						maximum = ContinuousFloatUtil.RoundToBindingDecimals(gmax),
						rangeMinimum = ContinuousFloatUtil.RoundToBindingDecimals(grMin),
						rangeMaximum = ContinuousFloatUtil.RoundToBindingDecimals(grMax),
						minimumFractionalDigits = gminDig,
						maximumFractionalDigits = gmaxDig,
						unit = string.IsNullOrWhiteSpace(editor.unit) ? null : editor.unit.Trim(),
					};
					if (!appendActions(editor, i, logf.actions, out UiTextFeedback? hkErr3))
						return (false, null, hkErr3);
					built.Add(logf);
					break;
				case BindingEditorType.LEVEL:
					if (!tryParseFloatWithDigits(editor.minimum, "Minimum", out float lvmin, out int lvminDig, out string? elvmin))
						return (false, null, feedback(elvmin));
					if (!tryParseFloatWithDigits(editor.maximum, "Maximum", out float lvmax, out int lvmaxDig, out string? elvmax))
						return (false, null, feedback(elvmax));
					if (lvmin > lvmax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					var level = new BindingLevel {
						name = editor.name.Trim(),
						address = editor.address.Trim(),
						minimum = ContinuousFloatUtil.RoundToBindingDecimals(lvmin),
						maximum = ContinuousFloatUtil.RoundToBindingDecimals(lvmax),
						minimumFractionalDigits = lvminDig,
						maximumFractionalDigits = lvmaxDig,
					};
					if (!appendActions(editor, i, level.actions, out UiTextFeedback? hkErr4))
						return (false, null, hkErr4);
					built.Add(level);
					break;
				case BindingEditorType.TOGGLE: {
					var toggle = new BindingToggle {
						name = editor.name.Trim(),
						address = editor.address.Trim(),
					};
					if (!appendActions(editor, i, toggle.actions, out UiTextFeedback? hkErr5))
						return (false, null, hkErr5);
					built.Add(toggle);
					break;
				}
			}
		}

		// No binding-type requirement: allow saving configs that only contain toggles (or no bindings yet).

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

	static bool appendActions(BindingEditor editor, int bindingIndex, List<ControlAction> target, out UiTextFeedback? error) {
		for (int h = 0; h < editor.actions.Count; h++) {
			ControlActionEditor hk = editor.actions[h];
			if (hk.isDeleted || isHotkeyRowBlank(hk))
				continue;
			if (!hk.tryBuildModel(editor.type, out ControlAction? action, out string? hkErr)) {
				error = new UiTextFeedback($"Binding {bindingIndex + 1}, hotkey {h + 1}: {hkErr}", UiTextFeedbackKind.ERROR);
				return false;
			}
			target.Add(action!);
		}
		error = null;
		return true;
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

	static bool tryParseFloatWithDigits(string text, string label, out float value, out int fractionalDigits, out string? error) {
		value = 0;
		fractionalDigits = 0;
		error = null;
		if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed)) {
			error = $"{label} must be a finite number.";
			return false;
		}
		value = parsed;
		fractionalDigits = ContinuousFloatUtil.fractionalDigitsOfTypedString(text);
		return true;
	}

	/// <summary>Both range fields blank → use limit; otherwise both must be valid numbers.</summary>
	static bool tryParseOptionalRange(
		string rangeMinText,
		string rangeMaxText,
		float limitMin,
		float limitMax,
		int bindingNumberOneBased,
		string kindLabel,
		out float rangeMin,
		out float rangeMax,
		out string? error) {
		rangeMin = limitMin;
		rangeMax = limitMax;
		error = null;
		bool minBlank = string.IsNullOrWhiteSpace(rangeMinText);
		bool maxBlank = string.IsNullOrWhiteSpace(rangeMaxText);
		if (minBlank && maxBlank)
			return true;
		if (minBlank || maxBlank) {
			error = $"Binding {bindingNumberOneBased}: {kindLabel} requires both range min and range max, or leave both empty to match minimum/maximum.";
			return false;
		}
		if (!tryParseFloatWithDigits(rangeMinText, "Range min", out rangeMin, out _, out error))
			return false;
		if (!tryParseFloatWithDigits(rangeMaxText, "Range max", out rangeMax, out _, out error))
			return false;
		return true;
	}

	static bool isBindingBlank(BindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& string.IsNullOrWhiteSpace(editor.minimum)
		&& string.IsNullOrWhiteSpace(editor.maximum)
		&& string.IsNullOrWhiteSpace(editor.rangeMinimum)
		&& string.IsNullOrWhiteSpace(editor.rangeMaximum)
		&& string.IsNullOrWhiteSpace(editor.unit)
		&& editor.actions.Count == 0;

	static bool isHotkeyRowBlank(ControlActionEditor hk) => hk.hotkey.isNone;
}
