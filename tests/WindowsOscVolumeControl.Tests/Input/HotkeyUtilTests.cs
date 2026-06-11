namespace WindowsOscVolumeControl.Tests;

public class HotkeyUtilTests {
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
}
