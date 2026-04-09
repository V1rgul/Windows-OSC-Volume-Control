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
}
