using System.Collections.Frozen;
using System.Diagnostics;

namespace WindowsOscVolumeControl;

/// <summary>Runtime OSC fader/toggle rows and hotkey → slot map built from tray configuration.</summary>
public sealed class BindingManager {
	/// <summary>OSC fader and toggle bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public Config() { }

		public Config(Config from) {
			ArgumentNullException.ThrowIfNull(from);
			bindings = from.bindings.Select(cloneBinding).ToList();
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

	volatile FrozenDictionary<HotkeyGesture, Slot> _byHotkey = FrozenDictionary<HotkeyGesture, Slot>.Empty;

	/// <summary>Rebuilds the snapshot from config. Duplicate keys: last write wins.</summary>
	internal void rebuildFromConfig(IEnumerable<BindingAbstract> bindings) {
		var map = new Dictionary<HotkeyGesture, Slot>();
		foreach (BindingAbstract b in bindings) {
			BindingAbstract row = b switch {
				BindingFader f => new BindingFader(f),
				BindingToggle t => new BindingToggle(t),
				_ => throw new InvalidOperationException("Unknown binding type: " + b.GetType().Name),
			};
			foreach (HotkeyAction ha in row.hotkeys) {
				if (ha.hotkey.isNone)
					continue;
				HotkeyGesture k = ha.hotkey;
				if (map.ContainsKey(k))
					AppTrace.BindingManager.TraceEvent(TraceEventType.Warning, 0, $"Duplicate hotkey {HotkeyUtil.format(k)}, overwriting");
				map[k] = new Slot(row, ha.clone());
			}
		}
		_byHotkey = map.ToFrozenDictionary();
	}

	internal bool hasSlotForHotkey(HotkeyGesture hotkey) =>
		_byHotkey.ContainsKey(hotkey);

	internal bool tryGetSlot(HotkeyGesture hotkey, out Slot slot) =>
		_byHotkey.TryGetValue(hotkey, out slot);
}
