namespace WindowsOscVolumeControl.Tests;

public class KeyboardHookTests {
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void resolveKeyUpTargets_strictCandidateWins(bool acceptMacroChordKeyOrder) {
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
	public void resolveKeyUpTargets_fallbackAllKeyCodeMatches_whenEnabled() {
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
	public void resolveKeyUpTargets_noFallback_whenDisabled() {
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
	public void deadlineGestureStillHeld_respectsAcceptMacroChordKeyOrderFlag(
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
}
