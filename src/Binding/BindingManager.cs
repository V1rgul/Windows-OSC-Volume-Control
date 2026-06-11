using System.Collections.Frozen;

namespace WindowsOscVolumeControl.Binding;

/// <summary>Short- vs long-press slot lists for one <see cref="HotkeyGesture"/>.</summary>
public readonly struct HotkeyDispatchTargets {
	public IReadOnlyList<BindingManager.Slot> shortPressSlots { get; init; }
	public IReadOnlyList<BindingManager.Slot> longPressSlots { get; init; }

	public bool hasAny => shortPressSlots.Count > 0 || longPressSlots.Count > 0;
}

/// <summary>Runtime OSC bindings and hotkey → slot map built from tray configuration.</summary>
public sealed class BindingManager {
	/// <summary>OSC bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public Config() { }

		public Config(Config from) {
			ArgumentNullException.ThrowIfNull(from);
			bindings = from.bindings.Select(cloneBinding).ToList();
		}

		static BindingAbstract cloneBinding(BindingAbstract b) => b switch {
			BindingLinear f => new BindingLinear(f),
			BindingLinf x => new BindingLinf(x),
			BindingLogf g => new BindingLogf(g),
			BindingLevel l => new BindingLevel(l),
			BindingToggle t => new BindingToggle(t),
			_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
		};

		public static BindingLinear createDefaultLinearBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/fader",
			minimum = 0f,
			maximum = 1f,
			actions = [
				new ControlActionContinuousDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_DOWN },
					delta = -0.02f,
				},
				new ControlActionContinuousDelta {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_UP },
					delta = 0.02f,
				},
			],
		};

		public static BindingToggle createDefaultToggleBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/on",
			actions = [
				new ControlActionToggleFlip {
					hotkey = new HotkeyGesture { keyCode = HotkeyGesture.VK_VOLUME_MUTE },
				},
			],
		};

		public List<BindingAbstract> bindings { get; set; } = [createDefaultLinearBinding(), createDefaultToggleBinding()];
	}

	/// <summary>One hotkey’s target binding and action.</summary>
	public readonly struct Slot {
		public BindingAbstract binding { get; }
		public ControlAction action { get; }

		public Slot(BindingAbstract binding, ControlAction action) {
			this.binding = binding;
			this.action = action;
		}
	}

	sealed class GestureBuckets {
		public readonly List<Slot> shortPress = [];
		public readonly List<Slot> longPress = [];
	}

	volatile FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets> _byGesture = FrozenDictionary<HotkeyGesture, HotkeyDispatchTargets>.Empty;
	volatile int[] _boundMainKeyCodes = [];

	/// <summary>Distinct main-key VK codes of all bound gestures; feeds the keyboard hook's lock-free fast path.</summary>
	internal IReadOnlyCollection<int> boundMainKeyCodes => _boundMainKeyCodes;

	/// <summary>Rebuilds the snapshot from config. Same gesture may appear in multiple rows and in both short and long buckets.</summary>
	internal void rebuildFromConfig(IEnumerable<BindingAbstract> bindings) {
		var merge = new Dictionary<HotkeyGesture, GestureBuckets>();
		foreach (BindingAbstract b in bindings) {
			BindingAbstract row = b switch {
				BindingLinear f => new BindingLinear(f),
				BindingLinf x => new BindingLinf(x),
				BindingLogf g => new BindingLogf(g),
				BindingLevel l => new BindingLevel(l),
				BindingToggle t => new BindingToggle(t),
				_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
			};
			foreach (ControlAction ha in row.actions) {
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

		_boundMainKeyCodes = frozenMap.Keys.Select(static g => g.keyCode).Distinct().ToArray();
		_byGesture = frozenMap.ToFrozenDictionary();
	}

	internal bool tryGetDispatchTargets(HotkeyGesture hotkey, out HotkeyDispatchTargets targets) {
		hotkey = HotkeyUtil.normalize(hotkey);
		return _byGesture.TryGetValue(hotkey, out targets);
	}
}
