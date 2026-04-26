namespace WindowsOscVolumeControl.Tests;

public class UnitTest1 {
	[Fact]
	public void RoundToBindingDecimals_UsesProjectBindingPrecision() {
		float value = 0.1236f;

		float rounded = ContinuousFloatUtil.RoundToBindingDecimals(value);

		Assert.Equal(0.124f, rounded);
	}

	[Theory]
	[InlineData(0.02f, 2)]
	[InlineData(0.2f, 1)]
	[InlineData(1f, 0)]
	public void GetOsdFractionalDigitsFromStep_MatchesRoundedStep(float step, int expectedDigits) {
		int digits = ContinuousFloatUtil.GetOsdFractionalDigitsFromStep(step);

		Assert.Equal(expectedDigits, digits);
	}

	[Fact]
	public void FormatOsdLevelValue_RespectsRequestedPrecision() {
		string formatted = ContinuousFloatUtil.FormatOsdLevelValue(0.1256f, 2);

		Assert.Equal("0.13", formatted);
	}

	[Fact]
	public void HotkeyUtil_RoundTripsCompoundHotkeys() {
		bool parsed = HotkeyUtil.tryParse("LeftCtrl+LeftShift+A", out HotkeyGesture hotkey);

		Assert.True(parsed);
		Assert.Equal("LeftCtrl+LeftShift+A", HotkeyUtil.format(hotkey));
	}

	[Fact]
	public void HotkeyUtil_CanonicalizesModifierOrderInFormat() {
		Assert.True(HotkeyUtil.tryParse("RightShift+LeftCtrl+A", out HotkeyGesture hotkey));

		Assert.Equal("LeftCtrl+RightShift+A", HotkeyUtil.format(hotkey));
	}

	[Fact]
	public void HotkeyUtil_RejectsLegacyGenericCtrlToken() {
		Assert.False(HotkeyUtil.tryParse("Ctrl+A", out _));
	}

	[Theory]
	[InlineData(HotkeyModifiers.NONE, HotkeyModifiers.NONE, true)]
	[InlineData(HotkeyModifiers.NONE, HotkeyModifiers.LEFT_CONTROL, false)]
	[InlineData(HotkeyModifiers.LEFT_CONTROL, HotkeyModifiers.LEFT_CONTROL, true)]
	[InlineData(HotkeyModifiers.LEFT_CONTROL, HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.RIGHT_CONTROL, true)]
	[InlineData(HotkeyModifiers.LEFT_CONTROL, HotkeyModifiers.RIGHT_CONTROL, false)]
	[InlineData(HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.LEFT_CONTROL, false)]
	[InlineData(HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.LEFT_SHIFT, true)]
	public void HotkeyUtil_ActiveSidesMatchGesture(HotkeyModifiers required, HotkeyModifiers active, bool expected) {
		bool ok = HotkeyUtil.activeSidesMatchGesture(required, active);

		Assert.Equal(expected, ok);
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
		BindingLinear linear = BindingManager.Config.createDefaultLinearBinding();
		BindingToggle toggle = BindingManager.Config.createDefaultToggleBinding();

		Assert.Equal(2, linear.actions.Count);
		Assert.Single(toggle.actions);
		var down = Assert.IsType<ControlActionContinuousDelta>(linear.actions[0]);
		var up = Assert.IsType<ControlActionContinuousDelta>(linear.actions[1]);
		Assert.Equal(HotkeyGesture.VK_VOLUME_DOWN, down.hotkey.keyCode);
		Assert.Equal(HotkeyGesture.VK_VOLUME_UP, up.hotkey.keyCode);
		Assert.Equal(-0.02f, down.delta);
		Assert.Equal(0.02f, up.delta);
		var flip = Assert.IsType<ControlActionToggleFlip>(toggle.actions[0]);
		Assert.Equal(HotkeyGesture.VK_VOLUME_MUTE, flip.hotkey.keyCode);
	}

	[Fact]
	public void BindingManager_MultipleRowsSameGesture_CollectsAllShortSlots() {
		Assert.True(HotkeyUtil.tryParse("LeftCtrl+A", out HotkeyGesture hk));
		var f1 = new BindingLinear {
			name = "F1",
			address = "/1",
			minimum = 0f,
			maximum = 1f,
			actions = [new ControlActionContinuousDelta { hotkey = hk, delta = -0.01f }],
		};
		var f2 = new BindingLinear {
			name = "F2",
			address = "/2",
			minimum = 0f,
			maximum = 1f,
			actions = [new ControlActionContinuousDelta { hotkey = hk, delta = 0.01f }],
		};
		var bm = new BindingManager();
		bm.rebuildFromConfig(new BindingAbstract[] { f1, f2 });
		Assert.True(bm.tryGetDispatchTargets(hk, out HotkeyDispatchTargets t));
		Assert.Equal(2, t.shortPressSlots.Count);
		Assert.Empty(t.longPressSlots);
	}

	[Fact]
	public void BindingManager_ShortAndLongSameGesture_SplitBuckets() {
		Assert.True(HotkeyUtil.tryParse("LeftCtrl+B", out HotkeyGesture hk));
		var f = new BindingLinear {
			name = "F",
			address = "/x",
			minimum = 0f,
			maximum = 1f,
			actions = [
				new ControlActionContinuousDelta { hotkey = hk, delta = -0.01f, longPress = false },
				new ControlActionContinuousDelta { hotkey = hk, delta = 0.05f, longPress = true },
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
	public void LoadTrayConfig_ParsesHotkeyAcceptMacroChordKeyOrderFalse() {
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

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void KeyboardHook_resolveKeyUpTargetsForTests_StrictCandidateWins(bool acceptMacroChordKeyOrder) {
		HotkeyGesture g = HotkeyUtil.normalize(new HotkeyGesture {
			keyCode = (int)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F11),
			modifiers = HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT,
		});
		var keys = new[] { g };
		IReadOnlyList<HotkeyGesture> resolved = KeyboardHook.resolveKeyUpTargetsForTests(
			vkCode: g.keyCode,
			modifierSidesAtKeyUp: g.modifiers,
			activePressKeys: keys,
			acceptMacroChordKeyOrder: acceptMacroChordKeyOrder);
		Assert.Single(resolved);
		Assert.Equal(g, resolved[0]);
	}

	[Fact]
	public void KeyboardHook_resolveKeyUpTargetsForTests_FallbackAllKeyCodeMatches_WhenEnabled() {
		int f11Vk = (int)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F11);
		HotkeyGesture g1 = HotkeyUtil.normalize(new HotkeyGesture { keyCode = f11Vk, modifiers = HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT });
		HotkeyGesture g2 = HotkeyUtil.normalize(new HotkeyGesture { keyCode = f11Vk, modifiers = HotkeyModifiers.LEFT_CONTROL | HotkeyModifiers.RIGHT_SHIFT });
		var keys = new[] { g2, g1 }; // reversed
		IReadOnlyList<HotkeyGesture> resolved = KeyboardHook.resolveKeyUpTargetsForTests(
			vkCode: f11Vk,
			modifierSidesAtKeyUp: HotkeyModifiers.NONE,
			activePressKeys: keys,
			acceptMacroChordKeyOrder: true);
		Assert.Equal(2, resolved.Count);
		Assert.Contains(g1, resolved);
		Assert.Contains(g2, resolved);
	}

	[Fact]
	public void KeyboardHook_resolveKeyUpTargetsForTests_NoFallbackWhenDisabled() {
		int f11Vk = (int)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.F11);
		HotkeyGesture g1 = HotkeyUtil.normalize(new HotkeyGesture { keyCode = f11Vk, modifiers = HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT });
		var keys = new[] { g1 };
		IReadOnlyList<HotkeyGesture> resolved = KeyboardHook.resolveKeyUpTargetsForTests(
			vkCode: f11Vk,
			modifierSidesAtKeyUp: HotkeyModifiers.NONE,
			activePressKeys: keys,
			acceptMacroChordKeyOrder: false);
		Assert.Empty(resolved);
	}

	[Theory]
	[InlineData(true, false, HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.NONE, false)]
	[InlineData(true, true, HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.NONE, true)]
	[InlineData(false, true, HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.NONE, false)]
	[InlineData(false, true, HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT, HotkeyModifiers.RIGHT_CONTROL | HotkeyModifiers.LEFT_SHIFT, true)]
	public void KeyboardHook_deadlineGestureStillHeld_RespectsFlag(
		bool acceptMacroChordKeyOrder,
		bool mainKeyHeld,
		HotkeyModifiers required,
		HotkeyModifiers activeSides,
		bool expected) {
		bool ok = KeyboardHook.deadlineGestureStillHeld(
			mainKeyHeld: mainKeyHeld,
			requiredModifiers: required,
			activeModifierSides: activeSides,
			acceptMacroChordKeyOrder: acceptMacroChordKeyOrder);
		Assert.Equal(expected, ok);
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
