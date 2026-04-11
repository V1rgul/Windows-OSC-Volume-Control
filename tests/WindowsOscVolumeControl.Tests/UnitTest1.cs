namespace WindowsOscVolumeControl.Tests;

public class UnitTest1 {
	[Fact]
	public void RoundToBindingDecimals_UsesProjectBindingPrecision() {
		float value = 0.1236f;

		float rounded = FaderFloatUtil.RoundToBindingDecimals(value);

		Assert.Equal(0.124f, rounded);
	}

	[Theory]
	[InlineData(0.02f, 2)]
	[InlineData(0.2f, 1)]
	[InlineData(1f, 0)]
	public void GetOsdFractionalDigitsFromStep_MatchesRoundedStep(float step, int expectedDigits) {
		int digits = FaderFloatUtil.GetOsdFractionalDigitsFromStep(step);

		Assert.Equal(expectedDigits, digits);
	}

	[Fact]
	public void FormatOsdLevelValue_RespectsRequestedPrecision() {
		string formatted = FaderFloatUtil.FormatOsdLevelValue(0.1256f, 2);

		Assert.Equal("0.13", formatted);
	}

	[Fact]
	public void HotkeyUtil_RoundTripsCompoundHotkeys() {
		bool parsed = HotkeyUtil.tryParse("Ctrl+Shift+A", out HotkeyGesture hotkey);

		Assert.True(parsed);
		Assert.Equal("Ctrl+Shift+A", HotkeyUtil.format(hotkey));
	}

	[Theory]
	[InlineData("VolumeUp", HotkeyGesture.VK_VOLUME_UP)]
	[InlineData("VolumeDown", HotkeyGesture.VK_VOLUME_DOWN)]
	[InlineData("VolumeMute", HotkeyGesture.VK_VOLUME_MUTE)]
	public void HotkeyUtil_ParsesMediaKeys(string text, int expectedKeyCode) {
		bool parsed = HotkeyUtil.tryParse(text, out HotkeyGesture hotkey);

		Assert.True(parsed);
		Assert.Equal(expectedKeyCode, hotkey.keyCode);
	}

	[Fact]
	public void BindingManager_DefaultBindings_UseVolumeKeys() {
		BindingFader fader = BindingManager.Config.createDefaultFaderBinding();
		BindingToggle toggle = BindingManager.Config.createDefaultToggleBinding();

		Assert.Equal(2, fader.hotkeys.Count);
		Assert.Single(toggle.hotkeys);
		var down = Assert.IsType<HotkeyActionFaderDelta>(fader.hotkeys[0]);
		var up = Assert.IsType<HotkeyActionFaderDelta>(fader.hotkeys[1]);
		Assert.Equal(HotkeyGesture.VK_VOLUME_DOWN, down.hotkey.keyCode);
		Assert.Equal(HotkeyGesture.VK_VOLUME_UP, up.hotkey.keyCode);
		Assert.Equal(-0.02f, down.delta);
		Assert.Equal(0.02f, up.delta);
		var flip = Assert.IsType<HotkeyActionToggleFlip>(toggle.hotkeys[0]);
		Assert.Equal(HotkeyGesture.VK_VOLUME_MUTE, flip.hotkey.keyCode);
	}

	[Fact]
	public void BindingManager_MultipleRowsSameGesture_CollectsAllShortSlots() {
		Assert.True(HotkeyUtil.tryParse("Ctrl+A", out HotkeyGesture hk));
		var f1 = new BindingFader {
			name = "F1",
			address = "/1",
			minimum = 0f,
			maximum = 1f,
			hotkeys = [new HotkeyActionFaderDelta { hotkey = hk, delta = -0.01f }],
		};
		var f2 = new BindingFader {
			name = "F2",
			address = "/2",
			minimum = 0f,
			maximum = 1f,
			hotkeys = [new HotkeyActionFaderDelta { hotkey = hk, delta = 0.01f }],
		};
		var bm = new BindingManager();
		bm.rebuildFromConfig(new BindingAbstract[] { f1, f2 });
		Assert.True(bm.tryGetDispatchTargets(hk, out HotkeyDispatchTargets t));
		Assert.Equal(2, t.shortPressSlots.Count);
		Assert.Empty(t.longPressSlots);
	}

	[Fact]
	public void BindingManager_ShortAndLongSameGesture_SplitBuckets() {
		Assert.True(HotkeyUtil.tryParse("Ctrl+B", out HotkeyGesture hk));
		var f = new BindingFader {
			name = "F",
			address = "/x",
			minimum = 0f,
			maximum = 1f,
			hotkeys = [
				new HotkeyActionFaderDelta { hotkey = hk, delta = -0.01f, longPress = false },
				new HotkeyActionFaderDelta { hotkey = hk, delta = 0.05f, longPress = true },
			],
		};
		var bm = new BindingManager();
		bm.rebuildFromConfig(new BindingAbstract[] { f });
		Assert.True(bm.tryGetDispatchTargets(hk, out HotkeyDispatchTargets t));
		Assert.Single(t.shortPressSlots);
		Assert.Single(t.longPressSlots);
	}

	[Fact]
	public void LoadTrayConfig_ParsesHotkeyGlobalsAndLongPress() {
		const string text = """
			ip=127.0.0.1
			port=10023
			hotkeyLongPressMs=600
			hotkeyOptimizeNonLongPressKeyDown=false
			hotkeySuppressKeyForLongPressOnly=true
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
		BindingManager.Config tray = cfg.trayApp;
		var toggle = Assert.IsType<BindingToggle>(Assert.Single(tray.bindings));
		var flip = Assert.IsType<HotkeyActionToggleFlip>(Assert.Single(toggle.hotkeys));
		Assert.True(flip.longPress);
	}

	[Theory]
	[InlineData(@"""C:\Program Files\App\a.exe"" --foo", @"C:\Program Files\App\a.exe")]
	[InlineData(@"C:\b\App.exe arg", @"C:\b\App.exe")]
	[InlineData(@"C:\b\App.exe", @"C:\b\App.exe")]
	[InlineData(@"""D:\x y\z.exe""", @"D:\x y\z.exe")]
	public void WindowsAutostart_tryParseRunCommandFirstExecutable_Parses(string raw, string expected) {
		bool ok = WindowsAutostart.tryParseRunCommandFirstExecutable(raw, out string? path);

		Assert.True(ok);
		Assert.Equal(expected, path);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData(@"""C:\unclosed", false)]
	[InlineData(@"""", false)]
	public void WindowsAutostart_tryParseRunCommandFirstExecutable_Rejects(string raw, bool expectOk) {
		bool ok = WindowsAutostart.tryParseRunCommandFirstExecutable(raw, out _);

		Assert.Equal(expectOk, ok);
	}

	[Fact]
	public void WindowsAutostart_pathsEqualForAutostart_IsOrdinalIgnoreCaseOnWindows() {
		Assert.True(WindowsAutostart.pathsEqualForAutostart(
			@"C:\Windows\System32\notepad.exe",
			@"c:/Windows/System32/NOTEPAD.EXE"));
		Assert.False(WindowsAutostart.pathsEqualForAutostart(
			@"C:\Windows\System32\notepad.exe",
			@"C:\Windows\System32\write.exe"));
	}
}
