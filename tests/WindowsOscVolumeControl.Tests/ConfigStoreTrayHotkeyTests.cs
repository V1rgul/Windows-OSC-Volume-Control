namespace WindowsOscVolumeControl.Tests;

public class ConfigStoreTrayHotkeyTests {
	[Fact]
	public void loadAppConfigFromKeyValueText_parsesHotkeyGlobalsAndLongPress() {
		const string text = """
			ip=127.0.0.1
			port=10023
			hotkeyLongPressMs=600
			hotkeyOptimizeNonLongPressKeyDown=false
			hotkeySuppressKeyForLongPressOnly=true
			hotkeyAcceptMacroChordKeyOrder=true
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			osc.0.hotkey.0.key=VolumeMute
			osc.0.hotkey.0.action=toggle
			osc.0.hotkey.0.longPress=true
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out _);
		Assert.Equal(600u, cfg.keyboardHook.longPressDurationMs);
		Assert.False(cfg.keyboardHook.optimizeNonLongPressKeyDown);
		Assert.True(cfg.keyboardHook.suppressKeyForLongPressOnlyGestures);
		Assert.True(cfg.keyboardHook.acceptMacroChordKeyOrder);
		BindingManager.Config tray = cfg.trayApp;
		var toggle = Assert.IsType<BindingToggle>(Assert.Single(tray.bindings));
		var flip = Assert.IsType<ControlActionToggleFlip>(Assert.Single(toggle.actions));
		Assert.True(flip.longPress);
	}

	[Fact]
	public void loadAppConfigFromKeyValueText_parsesHotkeyAcceptMacroChordKeyOrderFalse() {
		const string text = """
			ip=127.0.0.1
			port=10023
			hotkeyAcceptMacroChordKeyOrder=false
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			osc.0.hotkey.0.key=VolumeMute
			osc.0.hotkey.0.action=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out _);
		Assert.False(cfg.keyboardHook.acceptMacroChordKeyOrder);
	}
}
