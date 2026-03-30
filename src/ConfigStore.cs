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
	INVALID_OR_INCOMPLETE_FILE,
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
		appConfig = fromForm.deepClone();
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
			IReadOnlyDictionary<string, string> map = ParseKeyValueLines(File.ReadAllText(path));
			if (!map.TryGetValue("ip", out string? ipStr)
			    || !map.TryGetValue("port", out string? portStr)
			    || !OscConnectionConfigParse.TryParseIpPort(ipStr, portStr, out IPAddress ip, out int port, out _, out _)) {
				appConfig = new AppConfig();
				lastDiskOutcome = AppConfigDiskOutcome.INVALID_OR_INCOMPLETE_FILE;
				lastDiskFeedback = "Config file exists but could not be parsed; using defaults.";
				return;
			}
			var oscDefaults = new OscController.Config();
			uint timeout = oscDefaults.timeoutMs;
			if (map.TryGetValue("timeoutMs", out string? toStr)
			    && uint.TryParse(toStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint to)
			    && to >= OscController.Config.MIN_QUERY_TIMEOUT_MS)
				timeout = Math.Min(to, OscController.Config.MAX_QUERY_TIMEOUT_MS);
			var mixerDefaults = new MixerController.Config();
			uint valueCacheTtlMs = mixerDefaults.ValueCacheTtlMs;
			if (map.TryGetValue("valueCacheTtlMs", out string? ttlStr)
			    && uint.TryParse(ttlStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ttl))
				valueCacheTtlMs = Math.Min(ttl, MixerController.Config.MAX_VALUE_CACHE_TTL_MS);
			List<OscFaderBinding> faders = ParseOscFaderBindings(map);
			if (faders.Count == 0) {
				appConfig = new AppConfig();
				lastDiskOutcome = AppConfigDiskOutcome.INVALID_OR_INCOMPLETE_FILE;
				lastDiskFeedback = "Config file exists but could not be parsed; using defaults.";
				return;
			}
			List<OscToggleBinding> oscToggles = ParseOscToggleBindings(map);
			appConfig = new AppConfig {
				oscController = new OscController.Config {
					endPoint = new IPEndPoint(ip, port),
					timeoutMs = timeout,
				},
				mixer = new MixerController.Config {
					ValueCacheTtlMs = valueCacheTtlMs,
				},
				trayApp = new TrayApp.Config {
					faderBindings = faders,
					bindings = oscToggles,
				},
				osd = ParseOsdFromMap(map),
			};
			lastDiskOutcome = AppConfigDiskOutcome.LOADED_OK;
			lastDiskFeedback = "Loaded settings from disk.";
		} catch (Exception ex) {
			appConfig = new AppConfig();
			lastDiskOutcome = AppConfigDiskOutcome.LOAD_IO_ERROR;
			lastDiskFeedback = "Could not read config file: " + ex.Message;
		}
	}

	public void tryPersistToDisk() {
		AppConfig cfg = appConfig;
		var osc = cfg.oscController;
		var mixer = cfg.mixer;
		OSDController.Config osd = OSDController.Config.Clamped(cfg.osd);
		var toggles = cfg.trayApp?.bindings ?? [];
		var faders = cfg.trayApp?.faderBindings ?? [];
		var lines = new List<string> {
			"ip=" + osc.endPoint.Address.ToString(),
			"port=" + osc.endPoint.Port.ToString(CultureInfo.InvariantCulture),
			"timeoutMs=" + osc.timeoutMs.ToString(CultureInfo.InvariantCulture),
			"valueCacheTtlMs=" + mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture),
			"osdHeightPx=" + osd.HeightPx.ToString(CultureInfo.InvariantCulture),
			"osdDisplayDurationMs=" + osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture),
		};
		for (int i = 0; i < faders.Count; i++) {
			OscFaderBinding b = faders[i];
			string p = i.ToString(CultureInfo.InvariantCulture);
			lines.Add("oscFader." + p + ".name=" + b.name.Trim());
			lines.Add("oscFader." + p + ".address=" + b.address.Trim());
			lines.Add("oscFader." + p + ".step=" + FaderFloatUtil.FormatGridFloat(b.step));
			lines.Add("oscFader." + p + ".minimum=" + FaderFloatUtil.FormatGridFloat(b.minimum));
			lines.Add("oscFader." + p + ".maximum=" + FaderFloatUtil.FormatGridFloat(b.maximum));
			lines.Add("oscFader." + p + ".hotkeyMinus=" + OscHotkey.format(b.hotkeyMinus));
			lines.Add("oscFader." + p + ".hotkeyPlus=" + OscHotkey.format(b.hotkeyPlus));
		}
		for (int i = 0; i < toggles.Count; i++) {
			OscToggleBinding binding = toggles[i];
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".name=" + binding.name.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".address=" + binding.address.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".hotkey=" + OscHotkey.format(binding.hotkey));
		}
		try {
			File.WriteAllText(configPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
			lastDiskOutcome = AppConfigDiskOutcome.SAVED_OK;
			lastDiskFeedback = "Saved to disk.";
		} catch (Exception ex) {
			lastDiskOutcome = AppConfigDiskOutcome.SAVE_FAILED;
			lastDiskFeedback = "Save failed: " + ex.Message;
		}
	}

	static OSDController.Config ParseOsdFromMap(IReadOnlyDictionary<string, string> map) {
		var o = new OSDController.Config();
		if (map.TryGetValue("osdHeightPx", out string? hStr)
		    && int.TryParse(hStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
			o.HeightPx = h;
		if (map.TryGetValue("osdDisplayDurationMs", out string? dStr)
		    && uint.TryParse(dStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint d))
			o.DisplayDurationMs = d;
		return OSDController.Config.Clamped(o);
	}

	static Dictionary<string, string> ParseKeyValueLines(string text) {
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

	static List<OscFaderBinding> ParseOscFaderBindings(IReadOnlyDictionary<string, string> map) {
		var rows = new Dictionary<int, OscFaderBinding>();
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
			if (!rows.TryGetValue(index, out OscFaderBinding? row)) {
				row = new OscFaderBinding();
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
					if (OscHotkey.tryParse(value, out var hm))
						row.hotkeyMinus = hm;
					break;
				case "hotkeyplus":
					if (OscHotkey.tryParse(value, out var hp))
						row.hotkeyPlus = hp;
					break;
			}
		}

		var result = new List<OscFaderBinding>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			OscFaderBinding row = rows[index];
			if (string.IsNullOrWhiteSpace(row.name) || string.IsNullOrWhiteSpace(row.address))
				continue;
			if (!float.IsFinite(row.step) || !float.IsFinite(row.minimum) || !float.IsFinite(row.maximum) || row.minimum > row.maximum)
				continue;
			row.step = Math.Clamp(FaderFloatUtil.RoundToBindingDecimals(row.step), MixerController.Config.MIN_FADER_STEP, MixerController.Config.MAX_FADER_STEP);
			float minR = FaderFloatUtil.RoundToBindingDecimals(row.minimum);
			float maxR = FaderFloatUtil.RoundToBindingDecimals(row.maximum);
			result.Add(new OscFaderBinding {
				name = row.name.Trim(),
				address = row.address.Trim(),
				step = row.step,
				minimum = minR,
				maximum = maxR,
				hotkeyMinus = OscHotkey.normalize(row.hotkeyMinus),
				hotkeyPlus = OscHotkey.normalize(row.hotkeyPlus),
			});
		}
		return result;
	}

	static List<OscToggleBinding> ParseOscToggleBindings(IReadOnlyDictionary<string, string> map) {
		var rows = new Dictionary<int, OscToggleBinding>();
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
			if (!rows.TryGetValue(index, out OscToggleBinding? row)) {
				row = new OscToggleBinding();
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
					if (OscHotkey.tryParse(value, out var hotkey))
						row.hotkey = hotkey;
					break;
			}
		}

		var result = new List<OscToggleBinding>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			OscToggleBinding row = rows[index];
			if (string.IsNullOrWhiteSpace(row.name) || string.IsNullOrWhiteSpace(row.address) || row.hotkey == Keys.None)
				continue;
			result.Add(new OscToggleBinding {
				name = row.name.Trim(),
				address = row.address.Trim(),
				hotkey = OscHotkey.normalize(row.hotkey),
			});
		}
		return result;
	}
}
