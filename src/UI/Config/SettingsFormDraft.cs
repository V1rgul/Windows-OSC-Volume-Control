using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.UI.Config;

static class SettingsFormDraft {
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
			if (editor.isDeleted || editor.isBlank)
				continue;

			if (!editor.tryBuildMaterialized(out BindingAbstract? binding, out string? bindErr)) {
				return (false, null, new UiTextFeedback($"Binding {i + 1}: {bindErr}", UiTextFeedbackKind.ERROR));
			}
			built.Add(binding);
		}

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
}
