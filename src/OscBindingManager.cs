namespace WindowsOscVolumeControl;

/// <summary>Runtime OSC fader/toggle rows and hotkey → binding maps built from tray configuration.</summary>
public sealed class OscBindingManager {
	readonly object _sync = new();
	List<OscBindingFader> _faderBindings = [];
	Dictionary<Keys, OscBindingFader> _faderMinusByHotkey = [];
	Dictionary<Keys, OscBindingFader> _faderPlusByHotkey = [];
	List<OscBindingToggle> _oscToggleBindings = [];
	Dictionary<Keys, OscBindingToggle> _oscTogglesByHotkey = [];

	internal IReadOnlyList<OscBindingToggle> oscToggleBindings {
		get {
			lock (_sync)
				return _oscToggleBindings.Select(b => new OscBindingToggle(b)).ToArray();
		}
	}

	internal IReadOnlyList<OscBindingFader> OscFaderBindings {
		get {
			lock (_sync)
				return _faderBindings.Select(f => new OscBindingFader(f)).ToArray();
		}
	}

	/// <summary>Rebuilds maps from config and returns every hotkey the keyboard hook should watch.</summary>
	internal HashSet<Keys> rebuildFromConfig(IEnumerable<OscBindingFader> faders, IEnumerable<OscBindingToggle> toggles) {
		List<OscBindingFader> fd = faders.Select(f => new OscBindingFader(f)).ToList();
		List<OscBindingToggle> tg = toggles.Select(t => new OscBindingToggle(t) { hotkey = KeysUtil.normalize(t.hotkey) }).ToList();
		var minus = new Dictionary<Keys, OscBindingFader>();
		var plus = new Dictionary<Keys, OscBindingFader>();
		var toggleMap = new Dictionary<Keys, OscBindingToggle>();
		var allKeys = new HashSet<Keys>();
		foreach (OscBindingFader f in fd) {
			if (f.hotkeyMinus != Keys.None) {
				Keys k = KeysUtil.normalize(f.hotkeyMinus);
				minus[k] = f;
				allKeys.Add(k);
			}
			if (f.hotkeyPlus != Keys.None) {
				Keys k = KeysUtil.normalize(f.hotkeyPlus);
				plus[k] = f;
				allKeys.Add(k);
			}
		}
		foreach (OscBindingToggle t in tg) {
			if (t.hotkey == Keys.None)
				continue;
			Keys k = KeysUtil.normalize(t.hotkey);
			toggleMap[k] = t;
			allKeys.Add(k);
		}
		lock (_sync) {
			_faderBindings = fd;
			_faderMinusByHotkey = minus;
			_faderPlusByHotkey = plus;
			_oscToggleBindings = tg;
			_oscTogglesByHotkey = toggleMap;
		}
		return allKeys;
	}

	internal void tryGetForHotkey(Keys hotkey, out OscBindingFader? fPlus, out OscBindingFader? fMinus, out OscBindingToggle? toggle) {
		lock (_sync) {
			_faderPlusByHotkey.TryGetValue(hotkey, out fPlus);
			_faderMinusByHotkey.TryGetValue(hotkey, out fMinus);
			_oscTogglesByHotkey.TryGetValue(hotkey, out toggle);
		}
	}
}
