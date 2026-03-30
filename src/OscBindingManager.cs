using System.Collections.Frozen;
using System.Diagnostics;

namespace WindowsOscVolumeControl;

/// <summary>Runtime OSC fader/toggle rows and hotkey → slot map built from tray configuration.</summary>
public sealed class OscBindingManager {
	/// <summary>One hotkey’s target binding and action kind.</summary>
	public readonly struct Slot {
		public enum Kind {
			TOGGLE,
			UP,
			DOWN,
		}

		public OscBindingAbstract binding { get; }
		public Kind kind { get; }

		public Slot(OscBindingAbstract binding, Kind kind) {
			this.binding = binding;
			this.kind = kind;
		}
	}

	volatile FrozenDictionary<Keys, Slot> _byHotkey = FrozenDictionary<Keys, Slot>.Empty;

	/// <summary>Rebuilds the snapshot from config. Hotkeys are expected already normalized (e.g. ConfigStore / settings form). Duplicate keys (e.g. hand-edited file): TOGGLE then DOWN then UP per row, last write wins.</summary>
	internal void rebuildFromConfig(IEnumerable<OscBindingFader> faders, IEnumerable<OscBindingToggle> toggles) {
		var map = new Dictionary<Keys, Slot>();
		foreach (OscBindingToggle t in toggles) {
			var row = new OscBindingToggle(t);
			if (row.hotkey != Keys.None)
				map[row.hotkey] = new Slot(row, Slot.Kind.TOGGLE);
		}
		foreach (OscBindingFader f in faders) {
			var row = new OscBindingFader(f);
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
