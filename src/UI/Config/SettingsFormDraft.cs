using Result;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.UI.Config;

static class SettingsFormDraft {
	const string FOOTER_ERROR_SEPARATOR = "; ";

	static string footerErrors(global::Result.Result.Error[] errors) =>
		string.Join(FOOTER_ERROR_SEPARATOR, errors);

	public static (bool ok, AppConfig? config, UiTextFeedback? error) tryBuild(
		SettingsScalarsMaterialized scalars,
		OSDController.Config.OsdScreenAnchor osdScreenAnchor,
		bool hotkeyOptimizeNonLongPressKeyDown,
		bool hotkeySuppressKeyForLongPressOnlyGestures,
		bool hotkeyAcceptMacroChordKeyOrder,
		IReadOnlyList<BindingEditor> bindings) {
		var built = new List<BindingAbstract>();
		for (int i = 0; i < bindings.Count; i++) {
			BindingEditor editor = bindings[i];
			if (editor.isDeleted || isBindingBlank(editor))
				continue;

			if (!tryParseRequiredText(editor.name, i + 1, out string name, out UiTextFeedback? nameError))
				return (false, null, nameError);
			if (!tryParseOscAddress(editor.address, i + 1, out string address, out UiTextFeedback? addressError))
				return (false, null, addressError);

			switch (editor.type) {
				case BindingEditorType.LINEAR:
					if (!tryParseFloatWithDigits(editor.minimum, out float min, out int minDig, out string? emin))
						return (false, null, feedback(emin));
					if (!tryParseFloatWithDigits(editor.maximum, out float max, out int maxDig, out string? emax))
						return (false, null, feedback(emax));
					if (min > max)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					min = ContinuousFloatUtil.RoundToBindingDecimals(min);
					max = ContinuousFloatUtil.RoundToBindingDecimals(max);
					var linear = new BindingLinear {
						name = name,
						address = address,
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
					if (!tryParseFloatWithDigits(editor.minimum, out float lmin, out int lminDig, out string? elmin))
						return (false, null, feedback(elmin));
					if (!tryParseFloatWithDigits(editor.maximum, out float lmax, out int lmaxDig, out string? elmax))
						return (false, null, feedback(elmax));
					if (lmin > lmax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					if (!tryParseOptionalRange(editor.rangeMinimum, editor.rangeMaximum, lmin, lmax, i + 1, "linf", out float lrMin, out float lrMax, out string? rangeErr))
						return (false, null, feedback(rangeErr));
					if (lrMin > lrMax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: range min must be less than or equal to range max.", UiTextFeedbackKind.ERROR));
					var linf = new BindingLinf {
						name = name,
						address = address,
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
					if (!tryParseFloatWithDigits(editor.minimum, out float gmin, out int gminDig, out string? egmin))
						return (false, null, feedback(egmin));
					if (!tryParseFloatWithDigits(editor.maximum, out float gmax, out int gmaxDig, out string? egmax))
						return (false, null, feedback(egmax));
					if (gmin > gmax || gmin <= 0f || gmax <= 0f)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: logf requires positive minimum and maximum.", UiTextFeedbackKind.ERROR));
					if (!tryParseOptionalRange(editor.rangeMinimum, editor.rangeMaximum, gmin, gmax, i + 1, "logf", out float grMin, out float grMax, out string? gRangeErr))
						return (false, null, feedback(gRangeErr));
					if (grMin > grMax || grMin <= 0f || grMax <= 0f)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: logf range requires positive range min and range max.", UiTextFeedbackKind.ERROR));
					var logf = new BindingLogf {
						name = name,
						address = address,
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
					if (!tryParseFloatWithDigits(editor.minimum, out float lvmin, out int lvminDig, out string? elvmin))
						return (false, null, feedback(elvmin));
					if (!tryParseFloatWithDigits(editor.maximum, out float lvmax, out int lvmaxDig, out string? elvmax))
						return (false, null, feedback(elvmax));
					if (lvmin > lvmax)
						return (false, null, new UiTextFeedback($"Binding {i + 1}: minimum must be less than or equal to maximum.", UiTextFeedbackKind.ERROR));
					var level = new BindingLevel {
						name = name,
						address = address,
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
						name = name,
						address = address,
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
				endPoint = scalars.oscEndPoint,
			},
			mixer = new MixerController.Config {
				timeoutMs = scalars.queryTimeoutMs,
				ValueCacheTtlMs = scalars.valueCacheTtlMs,
			},
			osd = new OSDController.Config {
				heightDip = scalars.osdHeightDip,
				DisplayDurationMs = scalars.osdDisplayDurationMs,
				screenAnchor = OSDController.Config.clampScreenAnchor(osdScreenAnchor),
			},
			trayApp = new BindingManager.Config {
				bindings = built,
			},
			keyboardHook = KeyboardHook.Config.Clamped(new KeyboardHook.Config {
				longPressDurationMs = scalars.hotkeyLongPressMs,
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
			target.Add(action);
		}
		error = null;
		return true;
	}

	static UiTextFeedback feedback(string? message) =>
		new(message ?? "Invalid configuration.", UiTextFeedbackKind.ERROR);

	static bool tryParseRequiredText(string text, int bindingNumberOneBased, out string value, out UiTextFeedback? error) {
		Result<string> result = BindingManager.Config.parseBindingNameField(text);
		if (result.isSuccess) {
			value = result.value;
			error = null;
			return true;
		}
		value = "";
		error = new UiTextFeedback($"Binding {bindingNumberOneBased}: {footerErrors(result.errors)}", UiTextFeedbackKind.ERROR);
		return false;
	}

	static bool tryParseOscAddress(string text, int bindingNumberOneBased, out string value, out UiTextFeedback? error) {
		Result<string> result = BindingManager.Config.parseOscAddressField(text);
		if (result.isSuccess) {
			value = result.value;
			error = null;
			return true;
		}
		value = "";
		error = new UiTextFeedback($"Binding {bindingNumberOneBased}: {footerErrors(result.errors)}", UiTextFeedbackKind.ERROR);
		return false;
	}

	static bool tryParseFloatWithDigits(string text, out float value, out int fractionalDigits, out string? error) {
		value = 0;
		fractionalDigits = 0;
		error = null;
		Result<BindingManager.Config.FloatFieldValue> result = BindingManager.Config.parseContinuousFloatField(text);
		if (result.isError) {
			error = footerErrors(result.errors);
			return false;
		}
		value = result.value.value;
		fractionalDigits = result.value.fractionalDigits;
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
		if (!tryParseFloatWithDigits(rangeMinText, out rangeMin, out _, out error))
			return false;
		if (!tryParseFloatWithDigits(rangeMaxText, out rangeMax, out _, out error))
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
