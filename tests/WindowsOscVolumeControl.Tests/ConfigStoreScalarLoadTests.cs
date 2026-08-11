using System.Net;
using WindowsOscVolumeControl.UI.Config;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.Tests;

public class ConfigStoreScalarLoadTests {
	[Fact]
	public void loadAppConfigFromKeyValueText_outOfRangeTimeout_usesDefaultAndRepairNote() {
		const string text = """
			ip=127.0.0.1
			port=10023
			timeoutMs=50000
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(200u, cfg.mixer.timeoutMs);
		Assert.Contains(repairNotes, n => n.Contains("timeoutMs", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void loadAppConfigFromKeyValueText_outOfRangeOsdHeight_usesDefaultAndRepairNote() {
		const string text = """
			ip=127.0.0.1
			port=10023
			osdHeightDip=10
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(80, cfg.osd.heightDip);
		Assert.Contains(repairNotes, n => n.Contains("osdHeightDip", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void loadAppConfigFromKeyValueText_invalidIp_usesOscDefaultsAndRepairNote() {
		const string text = """
			ip=not-valid
			port=10023
			osc.0.name=T
			osc.0.address=/t
			osc.0.type=toggle
			""";
		AppConfig cfg = ConfigStore.loadAppConfigFromKeyValueTextForTests(text, out List<string> repairNotes);
		Assert.Equal(IPAddress.Parse("127.0.0.1"), cfg.oscTransport.endPoint.Address);
		Assert.Equal(10023, cfg.oscTransport.endPoint.Port);
		Assert.Contains(repairNotes, n => n.Contains("OSC IP/port", StringComparison.OrdinalIgnoreCase));
	}
}

public class ConfigWindowViewModelBuildTests {
	static ConfigWindowViewModel sampleVm() {
		var store = new ConfigStore();
		store.adoptAppConfig(new AppConfig {
			oscTransport = new OscTransport.Config {
				address = IPAddress.Loopback,
				port = 10023,
			},
			mixer = new MixerController.Config {
				timeoutMs = 200,
				ValueCacheTtlMs = 1000,
			},
			osd = new OSDController.Config {
				heightDip = 80,
				DisplayDurationMs = 1000,
				screenAnchor = OSDController.Config.OsdScreenAnchor.BOTTOM_CENTER,
			},
			keyboardHook = KeyboardHook.Config.Clamped(new KeyboardHook.Config {
				longPressDurationMs = 450,
				optimizeNonLongPressKeyDown = true,
				suppressKeyForLongPressOnlyGestures = false,
				acceptMacroChordKeyOrder = true,
			}),
		});
		var vm = new ConfigWindowViewModel(null!, store);
		vm.loadFromConfigStore();
		vm.bindings.Clear();
		return vm;
	}

	[Fact]
	public void tryBuildAppConfig_cachedScalarResults_buildsAppConfig() {
		ConfigWindowViewModel vm = sampleVm();
		Assert.True(vm.tryBuildAppConfig(out AppConfig config, out UiTextFeedback? error));
		Assert.Null(error);
		Assert.Equal(200u, config.mixer.timeoutMs);
		Assert.Equal(1000u, config.mixer.ValueCacheTtlMs);
		Assert.Equal(80, config.osd.heightDip);
		Assert.Equal(450u, config.keyboardHook.longPressDurationMs);
		Assert.Equal(OSDController.Config.OsdScreenAnchor.BOTTOM_CENTER, config.osd.screenAnchor);
		Assert.True(config.keyboardHook.optimizeNonLongPressKeyDown);
		Assert.False(config.keyboardHook.suppressKeyForLongPressOnlyGestures);
		Assert.True(config.keyboardHook.acceptMacroChordKeyOrder);
	}

	[Fact]
	public void tryBuildAppConfig_materializedBinding_buildsFromCachedParseResults() {
		ConfigWindowViewModel vm = sampleVm();
		var editor = new BindingEditor {
			name = "Gain",
			address = "/gain",
			minimum = "0",
			maximum = "1",
		};
		ControlActionEditor action = editor.createActionEditor();
		action.hotkey = HotkeyUtil.normalize(new HotkeyGesture { keyCode = 0x70, modifiers = HotkeyModifiers.NONE });
		action.floatValue = "0.1";
		editor.actions.Add(action);
		vm.bindings.Add(editor);

		Assert.False(editor.HasErrors);
		Assert.False(action.HasErrors);

		Assert.True(vm.tryBuildAppConfig(out AppConfig config, out UiTextFeedback? error));
		Assert.Null(error);
		BindingLinear linear = Assert.IsType<BindingLinear>(Assert.Single(config.trayApp.bindings));
		Assert.Equal("Gain", linear.name);
		Assert.Equal("/gain", linear.address);
		Assert.Equal(0.1f, Assert.IsType<ControlActionContinuousDelta>(linear.actions[0]).delta);
	}
}
