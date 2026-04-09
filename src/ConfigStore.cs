using System.Globalization;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

/// <summary>Result of the last config load or save; drives UI message and severity.</summary>
public enum AppConfigDiskOutcome {
	NONE = 0,
	NO_FILE_USING_DEFAULTS,
	LOADED_OK,
	/// <summary>Config loaded from disk with at least one section taken from defaults (partial apply).</summary>
	LOADED_PARTIAL,
	LOAD_IO_ERROR,
	SAVED_OK,
	SAVE_FAILED,
}

/// <summary>Loads and persists <see cref="AppConfig"/>; owns the in-memory snapshot.</summary>
public sealed class ConfigStore {
	const string FILE_NAME = "Windows-OSC-Volume-Control.config";

	public string configPath => Path.Combine(AppContext.BaseDirectory, FILE_NAME);

	public AppConfig appConfig { get; private set; }

	public string lastDiskFeedback { get; private set; } = "";

	public AppConfigDiskOutcome lastDiskOutcome { get; private set; } = AppConfigDiskOutcome.NONE;

	public ConfigStore() {
		appConfig = new AppConfig();
	}

	public void adoptAppConfig(AppConfig fromForm) {
		ArgumentNullException.ThrowIfNull(fromForm);
		appConfig = new AppConfig(fromForm);
	}

	public void loadFromDisk() {
		string path = configPath;
		if (!File.Exists(path)) {
			appConfig = new AppConfig();
			lastDiskOutcome = AppConfigDiskOutcome.NO_FILE_USING_DEFAULTS;
			lastDiskFeedback = "No config file; using defaults.";
			return;
		}
		try {
			IReadOnlyDictionary<string, string> map = parseKeyValueLines(File.ReadAllText(path));
			var repairNotes = new List<string>();
			OscTransport.Config osc = buildOscConfigFromMap(map, repairNotes);
			MixerController.Config mixer = buildMixerConfigFromMap(map, repairNotes);
			BindingManager.Config bindingConfig = buildBindingManagerConfigFromMap(map, repairNotes);
			appConfig = new AppConfig {
				oscTransport = osc,
				mixer = mixer,
				trayApp = bindingConfig,
				osd = buildOsdConfigFromMap(map, repairNotes),
			};
			lastDiskOutcome = repairNotes.Count > 0
				? AppConfigDiskOutcome.LOADED_PARTIAL
				: AppConfigDiskOutcome.LOADED_OK;
			lastDiskFeedback = repairNotes.Count > 0
				? "Loaded settings from disk. " + string.Join(" ", repairNotes)
				: "Loaded settings from disk.";
		} catch (Exception ex) {
			appConfig = new AppConfig();
			lastDiskOutcome = AppConfigDiskOutcome.LOAD_IO_ERROR;
			lastDiskFeedback = "Could not read config file: " + ex.Message;
		}
	}

	public void tryPersistToDisk() {
		AppConfig cfg = appConfig;
		var osc = cfg.oscTransport;
		var mixer = cfg.mixer;
		OSDController.Config osd = OSDController.Config.Clamped(cfg.osd);
		var toggles = cfg.trayApp?.bindings ?? [];
		var faders = cfg.trayApp?.faderBindings ?? [];
		var lines = new List<string> {
			"ip=" + osc.endPoint.Address.ToString(),
			"port=" + osc.endPoint.Port.ToString(CultureInfo.InvariantCulture),
			"timeoutMs=" + mixer.timeoutMs.ToString(CultureInfo.InvariantCulture),
			"valueCacheTtlMs=" + mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture),
			"osdHeightPx=" + osd.HeightPx.ToString(CultureInfo.InvariantCulture),
			"osdDisplayDurationMs=" + osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture),
		};
		for (int i = 0; i < faders.Count; i++) {
			BindingFader b = faders[i];
			string p = i.ToString(CultureInfo.InvariantCulture);
			lines.Add("oscFader." + p + ".name=" + b.name.Trim());
			lines.Add("oscFader." + p + ".address=" + b.address.Trim());
			lines.Add("oscFader." + p + ".step=" + FaderFloatUtil.FormatGridFloat(b.step));
			lines.Add("oscFader." + p + ".minimum=" + FaderFloatUtil.FormatGridFloat(b.minimum));
			lines.Add("oscFader." + p + ".maximum=" + FaderFloatUtil.FormatGridFloat(b.maximum));
			lines.Add("oscFader." + p + ".hotkeyMinus=" + KeysUtil.format(b.hotkeyMinus));
			lines.Add("oscFader." + p + ".hotkeyPlus=" + KeysUtil.format(b.hotkeyPlus));
		}
		for (int i = 0; i < toggles.Count; i++) {
			BindingToggle binding = toggles[i];
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".name=" + binding.name.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".address=" + binding.address.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".hotkey=" + KeysUtil.format(binding.hotkey));
		}
		string path = configPath;
		string tmpPath = path + ".tmp";
		try {
			File.WriteAllText(tmpPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
			File.Move(tmpPath, path, true);
			lastDiskOutcome = AppConfigDiskOutcome.SAVED_OK;
			lastDiskFeedback = "Saved to disk.";
		} catch (Exception ex) {
			try {
				if (File.Exists(tmpPath))
					File.Delete(tmpPath);
			} catch {
				// Best-effort cleanup only.
			}
			lastDiskOutcome = AppConfigDiskOutcome.SAVE_FAILED;
			lastDiskFeedback = "Save failed: " + ex.Message;
		}
	}

	static List<BindingFader> cloneDefaultFaderBindings() {
		var trayDefaults = new BindingManager.Config();
		return trayDefaults.faderBindings.Select(f => new BindingFader(f)).ToList();
	}

	static List<BindingToggle> cloneDefaultToggleBindings() {
		var trayDefaults = new BindingManager.Config();
		return trayDefaults.bindings.Select(t => new BindingToggle(t)).ToList();
	}

	static BindingManager.Config buildBindingManagerConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		List<BindingFader> faders = parseOscFaderBindings(map);
		if (faders.Count == 0) {
			faders = cloneDefaultFaderBindings();
			repairNotes.Add("No valid fader bindings in file; using defaults.");
		}
		List<BindingToggle> toggles = parseOscToggleBindings(map);
		if (toggles.Count == 0) {
			toggles = cloneDefaultToggleBindings();
			repairNotes.Add("No valid toggle bindings in file; using defaults.");
		}
		return new BindingManager.Config {
			faderBindings = faders,
			bindings = toggles,
		};
	}

	static OscTransport.Config buildOscConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		if (map.TryGetValue("ip", out string? ipStr)
		    && map.TryGetValue("port", out string? portStr)
		    && OscConnectionConfigParse.tryParseIpPort(ipStr, portStr, out IPAddress ip, out int port, out _, out _)) {
			return new OscTransport.Config {
				endPoint = new IPEndPoint(ip, port),
			};
		}
		repairNotes.Add("OSC IP/port missing or invalid; using connection defaults.");
		return new OscTransport.Config();
	}

	static MixerController.Config buildMixerConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var mixerDefaults = new MixerController.Config();
		uint timeoutMs = mixerDefaults.timeoutMs;
		if (map.TryGetValue("timeoutMs", out string? toStr)) {
			if (uint.TryParse(toStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint to)
			    && to >= MixerController.Config.MIN_TIMEOUT_MS)
				timeoutMs = Math.Min(to, MixerController.Config.MAX_TIMEOUT_MS);
			else
				repairNotes.Add("timeoutMs invalid; using default.");
		}

		uint valueCacheTtlMs = mixerDefaults.ValueCacheTtlMs;
		if (map.TryGetValue("valueCacheTtlMs", out string? ttlStr)) {
			if (uint.TryParse(ttlStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ttl))
				valueCacheTtlMs = Math.Min(ttl, MixerController.Config.MAX_VALUE_CACHE_TTL_MS);
			else
				repairNotes.Add("valueCacheTtlMs invalid; using default.");
		}
		return new MixerController.Config {
			timeoutMs = timeoutMs,
			ValueCacheTtlMs = valueCacheTtlMs,
		};
	}

	static OSDController.Config buildOsdConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var o = new OSDController.Config();
		if (map.TryGetValue("osdHeightPx", out string? hStr)) {
			if (!int.TryParse(hStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
				repairNotes.Add("osdHeightPx invalid; ignored.");
			else
				o.HeightPx = h;
		}
		if (map.TryGetValue("osdDisplayDurationMs", out string? dStr)) {
			if (!uint.TryParse(dStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint d))
				repairNotes.Add("osdDisplayDurationMs invalid; ignored.");
			else
				o.DisplayDurationMs = d;
		}
		OSDController.Config clamped = OSDController.Config.Clamped(o);
		if (clamped.HeightPx != o.HeightPx || clamped.DisplayDurationMs != o.DisplayDurationMs)
			repairNotes.Add("OSD size or duration was out of range; clamped.");
		return clamped;
	}

	static Dictionary<string, string> parseKeyValueLines(string text) {
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)) {
			string line = raw.Trim();
			if (line.Length == 0 || line[0] == '#')
				continue;
			int eq = line.IndexOf('=');
			if (eq <= 0)
				continue;
			string key = line[..eq].Trim();
			string value = line[(eq + 1)..].Trim();
			if (key.Length > 0)
				map[key] = value;
		}
		return map;
	}

	static List<BindingFader> parseOscFaderBindings(IReadOnlyDictionary<string, string> map) {
		var rows = new Dictionary<int, BindingFader>();
		foreach ((string key, string value) in map) {
			if (!key.StartsWith("oscFader.", StringComparison.OrdinalIgnoreCase))
				continue;
			string rest = key["oscFader.".Length..];
			int dot = rest.IndexOf('.');
			if (dot <= 0)
				continue;
			if (!int.TryParse(rest[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
				continue;
			string field = rest[(dot + 1)..];
			if (!rows.TryGetValue(index, out BindingFader? row)) {
				row = new BindingFader();
				rows[index] = row;
			}
			switch (field.ToLowerInvariant()) {
				case "name":
					row.name = value.Trim();
					break;
				case "address":
					row.address = value.Trim();
					break;
				case "step":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float st) && float.IsFinite(st))
						row.step = st;
					break;
				case "minimum":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mn) && float.IsFinite(mn))
						row.minimum = mn;
					break;
				case "maximum":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mx) && float.IsFinite(mx))
						row.maximum = mx;
					break;
				case "hotkeyminus":
					if (KeysUtil.tryParse(value, out var hm))
						row.hotkeyMinus = hm;
					break;
				case "hotkeyplus":
					if (KeysUtil.tryParse(value, out var hp))
						row.hotkeyPlus = hp;
					break;
			}
		}

		var result = new List<BindingFader>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			BindingFader row = rows[index];
			if (string.IsNullOrWhiteSpace(row.name) || string.IsNullOrWhiteSpace(row.address))
				continue;
			if (!float.IsFinite(row.step) || !float.IsFinite(row.minimum) || !float.IsFinite(row.maximum) || row.minimum > row.maximum)
				continue;
			row.step = Math.Clamp(FaderFloatUtil.RoundToBindingDecimals(row.step), MixerController.Config.MIN_FADER_STEP, MixerController.Config.MAX_FADER_STEP);
			float minR = FaderFloatUtil.RoundToBindingDecimals(row.minimum);
			float maxR = FaderFloatUtil.RoundToBindingDecimals(row.maximum);
			result.Add(new BindingFader {
				name = row.name.Trim(),
				address = row.address.Trim(),
				step = row.step,
				minimum = minR,
				maximum = maxR,
				hotkeyMinus = KeysUtil.normalize(row.hotkeyMinus),
				hotkeyPlus = KeysUtil.normalize(row.hotkeyPlus),
			});
		}
		return result;
	}

	static List<BindingToggle> parseOscToggleBindings(IReadOnlyDictionary<string, string> map) {
		var rows = new Dictionary<int, BindingToggle>();
		foreach ((string key, string value) in map) {
			if (!key.StartsWith("oscToggle.", StringComparison.OrdinalIgnoreCase))
				continue;
			string rest = key["oscToggle.".Length..];
			int dot = rest.IndexOf('.');
			if (dot <= 0)
				continue;
			if (!int.TryParse(rest[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) || index < 0)
				continue;
			string field = rest[(dot + 1)..];
			if (!rows.TryGetValue(index, out BindingToggle? row)) {
				row = new BindingToggle();
				rows[index] = row;
			}
			switch (field.ToLowerInvariant()) {
				case "name":
					row.name = value.Trim();
					break;
				case "address":
					row.address = value.Trim();
					break;
				case "hotkey":
					if (KeysUtil.tryParse(value, out var hotkey))
						row.hotkey = hotkey;
					break;
			}
		}

		var result = new List<BindingToggle>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			BindingToggle row = rows[index];
			if (string.IsNullOrWhiteSpace(row.name) || string.IsNullOrWhiteSpace(row.address) || row.hotkey == Keys.None)
				continue;
			result.Add(new BindingToggle {
				name = row.name.Trim(),
				address = row.address.Trim(),
				hotkey = KeysUtil.normalize(row.hotkey),
			});
		}
		return result;
	}
}
