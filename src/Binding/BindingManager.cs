using System.Collections.Frozen;

namespace WindowsOscVolumeControl;

/// <summary>Short- vs long-press slot lists for one <see cref="HotkeyGesture"/>.</summary>
public readonly struct HotkeyDispatchTargets {
	public IReadOnlyList<BindingManager.Slot> shortPressSlots { get; init; }
	public IReadOnlyList<BindingManager.Slot> longPressSlots { get; init; }

	public bool hasAny => shortPressSlots.Count > 0 || longPressSlots.Count > 0;
}

/// <summary>Runtime OSC fader/toggle rows and hotkey → slot map built from tray configuration.</summary>
public sealed class BindingManager {
	/// <summary>OSC fader and toggle bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public const uint DEFAULT_LONG_PRESS_MS = 450;
		public const uint MIN_LONG_PRESS_MS = 50;
		public const uint MAX_LONG_PRESS_MS = 5000;

		public Config() { }

		public Config(Config from) {
			ArgumentNullException.ThrowIfNull(from);
			bindings = from.bindings.Select(cloneBinding).ToList();
			longPressDurationMs = from.longPressDurationMs;
			optimizeNonLongPressKeyDown = from.optimizeNonLongPressKeyDown;
		}

		static BindingAbstract cloneBinding(BindingAbstract b) => b switch {
			BindingFader f => new BindingFader(f),
			BindingToggle t => new BindingToggle(t),
			_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
		};

		public static BindingFader createDefaultFaderBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/fader",
			minimum = 0f,
			maximum = 1f,
			hotkeys = [
				new HotkeyActionFaderDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_DOWN },
					delta = -0.02f,
				},
				new HotkeyActionFaderDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_UP },
					delta = 0.02f,
				},
			],
		};

		public static BindingToggle createDefaultToggleBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/on",
			hotkeys = [
				new HotkeyActionToggleFlip {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_MUTE },
				},
			],
		};

		public List<BindingAbstract> bindings { get; set; } = [createDefaultFaderBinding(), createDefaultToggleBinding()];

		public uint longPressDurationMs { get; set; } = DEFAULT_LONG_PRESS_MS;

		/// <summary>When true, short-press rows fire on keydown (unless long-press rows exist for the same gesture).</summary>
		public bool optimizeNonLongPressKeyDown { get; set; } = true;

		public static uint clampLongPressDurationMs(uint ms) =>
			Math.Clamp(ms, MIN_LONG_PRESS_MS, MAX_LONG_PRESS_MS);
	}

	/// <summary>One hotkey’s target binding and action.</summary>
	public readonly struct Slot {
		public BindingAbstract binding { get; }
		public HotkeyAction action { get; }

		public Slot(BindingAbstract binding, HotkeyAction action) {
			this.binding = binding;
			this.action = action;
		}
	}

	sealed class GestureBuckets {
		public readonly List<Slot> shortPress = [];
		public readonly List<Slot> longPress = [];
	}

	volatile FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets> _byGesture = FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets>.Empty;

	/// <summary>Rebuilds the snapshot from config. Same gesture may appear in multiple rows and in both short and long buckets.</summary>
	internal void rebuildFromConfig(IEnumerable<BindingAbstract> bindings) {
		var merge = new Dictionary<HotkeyGesture, GestureBuckets>();
		foreach (BindingAbstract b in bindings) {
			BindingAbstract row = b switch {
				BindingFader f => new BindingFader(f),
				BindingToggle t => new BindingToggle(t),
				_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
			};
			foreach (HotkeyAction ha in row.hotkeys) {
				if (ha.hotkey.isNone)
					continue;
				HotkeyGesture k = HotkeyUtil.normalize(ha.hotkey);
				if (k.isNone)
					continue;
				if (!merge.TryGetValue(k, out GestureBuckets? buckets)) {
					buckets = new GestureBuckets();
					merge[k] = buckets;
				}
				var slot = new Slot(row, ha.clone());
				if (ha.longPress)
					buckets.longPress.Add(slot);
				else
					buckets.shortPress.Add(slot);
			}
		}

		var frozenMap = new Dictionary<HotkeyGesture, HotkeyDispatchTargets>();
		foreach ((HotkeyGesture g, GestureBuckets b) in merge) {
			HotkeyDispatchTargets t = new() {
				shortPressSlots = b.shortPress.Count > 0 ? b.shortPress.ToArray() : Array.Empty<Slot>(),
				longPressSlots = b.longPress.Count > 0 ? b.longPress.ToArray() : Array.Empty<Slot>(),
			};
			if (t.hasAny)
				frozenMap[g] = t;
		}

		_byGesture = frozenMap.ToFrozenDictionary();
	}

	internal bool tryGetDispatchTargets(HotkeyGesture hotkey, out HotkeyDispatchTargets targets) {
		hotkey = HotkeyUtil.normalize(hotkey);
		return _byGesture.TryGetValue(hotkey, out targets);
	}
}
