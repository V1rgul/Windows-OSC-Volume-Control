namespace WindowsOscVolumeControl.Tests;

public class BindingManagerTests {
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
}
