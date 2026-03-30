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
			faderBindings = from.faderBindings.Select(f => new BindingFader(f)).ToList();
			bindings = from.bindings.Select(b => new BindingToggle(b)).ToList();
		}

		/// <summary>Default out-of-box fader row (cosmetic name only; not resolved by code).</summary>
		public static BindingFader createDefaultFaderBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/fader",
			step = 0.02f,
			minimum = 0f,
			maximum = 1f,
			hotkeyMinus = Keys.VolumeDown,
			hotkeyPlus = Keys.VolumeUp,
		};

		public static BindingToggle createDefaultToggleBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/on",
			hotkey = Keys.VolumeMute,
		};

		public List<BindingFader> faderBindings { get; set; } = [createDefaultFaderBinding()];
		public List<BindingToggle> bindings { get; set; } = [createDefaultToggleBinding()];
	}

	/// <summary>One hotkey’s target binding and action kind.</summary>
	public readonly struct Slot {
		public enum Kind {
			TOGGLE,
			UP,
			DOWN,
		}

		public BindingAbstract binding { get; }
		public Kind kind { get; }

		public Slot(BindingAbstract binding, Kind kind) {
			this.binding = binding;
			this.kind = kind;
		}
	}

	volatile FrozenDictionary<Keys, Slot> _byHotkey = FrozenDictionary<Keys, Slot>.Empty;

	/// <summary>Rebuilds the snapshot from config. Hotkeys are expected already normalized (e.g. ConfigStore / settings form). Duplicate keys (e.g. hand-edited file): TOGGLE then DOWN then UP per row, last write wins.</summary>
	internal void rebuildFromConfig(IEnumerable<BindingFader> faders, IEnumerable<BindingToggle> toggles) {
		var map = new Dictionary<Keys, Slot>();
		foreach (BindingToggle t in toggles) {
			var row = new BindingToggle(t);
			if (row.hotkey != Keys.None)
				map[row.hotkey] = new Slot(row, Slot.Kind.TOGGLE);
		}
		foreach (BindingFader f in faders) {
			var row = new BindingFader(f);
			if (row.hotkeyMinus != Keys.None) {
				Keys k = row.hotkeyMinus;
				if (map.ContainsKey(k))
					Trace.WriteLine("OscBindingManager: duplicate hotkey " + k + ", overwriting with fader DOWN");
				map[k] = new Slot(row, Slot.Kind.DOWN);
			}
			if (row.hotkeyPlus != Keys.None) {
				Keys k = row.hotkeyPlus;
				if (map.ContainsKey(k))
					Trace.WriteLine("OscBindingManager: duplicate hotkey " + k + ", overwriting with fader UP");
				map[k] = new Slot(row, Slot.Kind.UP);
			}
		}
		_byHotkey = map.ToFrozenDictionary();
	}

	internal bool hasSlotForHotkey(Keys hotkey) =>
		_byHotkey.ContainsKey(hotkey);

	internal bool tryGetSlot(Keys hotkey, out Slot slot) =>
		_byHotkey.TryGetValue(hotkey, out slot);
}
