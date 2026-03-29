using System.Globalization;
using System.Linq;
using System.Net;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

/// <summary>Result of the last config load or save; drives UI message and severity.</summary>
public enum AppConfigDiskOutcome {
	None = 0,
	NoFileUsingDefaults,
	LoadedOk,
	InvalidOrIncompleteFile,
	LoadIoError,
	SavedOk,
	SaveFailed,
}

/// <summary>Loads and persists <see cref="AppConfig"/>; owns the in-memory snapshot.</summary>
public sealed class ConfigStore {
	const string FileName = "Windows-OSC-Volume-Control.config";

	public string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

	public AppConfig AppConfig { get; private set; }

	public string LastDiskFeedback { get; private set; } = "";

	public AppConfigDiskOutcome LastDiskOutcome { get; private set; } = AppConfigDiskOutcome.None;

	public ConfigStore() {
		AppConfig = new AppConfig();
	}

	public void AdoptAppConfig(AppConfig fromForm) {
		ArgumentNullException.ThrowIfNull(fromForm);
		AppConfig = fromForm.DeepClone();
	}

	public void LoadFromDisk() {
		string path = ConfigPath;
		if (!File.Exists(path)) {
			AppConfig = new AppConfig();
			LastDiskOutcome = AppConfigDiskOutcome.NoFileUsingDefaults;
			LastDiskFeedback = "No config file; using defaults.";
			return;
		}
		try {
			IReadOnlyDictionary<string, string> map = ParseKeyValueLines(File.ReadAllText(path));
			if (!map.TryGetValue("ip", out string? ipStr)
			    || !map.TryGetValue("port", out string? portStr)
			    || !OscConnectionConfigParse.TryParseIpPort(ipStr, portStr, out IPAddress ip, out int port, out _, out _)) {
				AppConfig = new AppConfig();
				LastDiskOutcome = AppConfigDiskOutcome.InvalidOrIncompleteFile;
				LastDiskFeedback = "Config file exists but could not be parsed; using defaults.";
				return;
			}
			var oscDefaults = new OscController.Config();
			uint timeout = oscDefaults.timeoutMs;
			if (map.TryGetValue("timeoutMs", out string? toStr)
			    && uint.TryParse(toStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint to)
			    && to >= OscController.Config.MinQueryTimeoutMs)
				timeout = Math.Min(to, OscController.Config.MaxQueryTimeoutMs);
			var mixerDefaults = new MixerController.Config();
			uint valueCacheTtlMs = mixerDefaults.ValueCacheTtlMs;
			if (map.TryGetValue("valueCacheTtlMs", out string? ttlStr)
			    && uint.TryParse(ttlStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ttl))
				valueCacheTtlMs = Math.Min(ttl, MixerController.Config.MaxValueCacheTtlMs);
			List<OscFaderBinding> faders = ParseOscFaderBindings(map);
			if (faders.Count == 0) {
				AppConfig = new AppConfig();
				LastDiskOutcome = AppConfigDiskOutcome.InvalidOrIncompleteFile;
				LastDiskFeedback = "Config file exists but could not be parsed; using defaults.";
				return;
			}
			List<OscToggleBinding> oscToggles = ParseOscToggleBindings(map);
			AppConfig = new AppConfig {
				OscController = new OscController.Config {
					EndPoint = new IPEndPoint(ip, port),
					timeoutMs = timeout,
				},
				Mixer = new MixerController.Config {
					ValueCacheTtlMs = valueCacheTtlMs,
				},
				TrayApp = new TrayApp.Config {
					FaderBindings = faders,
					Bindings = oscToggles,
				},
			};
			LastDiskOutcome = AppConfigDiskOutcome.LoadedOk;
			LastDiskFeedback = "Loaded settings from disk.";
		} catch (Exception ex) {
			AppConfig = new AppConfig();
			LastDiskOutcome = AppConfigDiskOutcome.LoadIoError;
			LastDiskFeedback = "Could not read config file: " + ex.Message;
		}
	}

	public void TryPersistToDisk() {
		AppConfig cfg = AppConfig;
		var osc = cfg.OscController;
		var mixer = cfg.Mixer ?? new MixerController.Config();
		var toggles = cfg.TrayApp?.Bindings ?? [];
		var faders = cfg.TrayApp?.FaderBindings ?? [];
		var lines = new List<string> {
			"ip=" + osc.EndPoint.Address.ToString(),
			"port=" + osc.EndPoint.Port.ToString(CultureInfo.InvariantCulture),
			"timeoutMs=" + osc.timeoutMs.ToString(CultureInfo.InvariantCulture),
			"valueCacheTtlMs=" + mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture),
		};
		for (int i = 0; i < faders.Count; i++) {
			OscFaderBinding b = faders[i];
			string p = i.ToString(CultureInfo.InvariantCulture);
			lines.Add("oscFader." + p + ".name=" + b.Name.Trim());
			lines.Add("oscFader." + p + ".address=" + b.Address.Trim());
			lines.Add("oscFader." + p + ".step=" + FaderFloatUtil.FormatGridFloat(b.Step));
			lines.Add("oscFader." + p + ".minimum=" + FaderFloatUtil.FormatGridFloat(b.Minimum));
			lines.Add("oscFader." + p + ".maximum=" + FaderFloatUtil.FormatGridFloat(b.Maximum));
			lines.Add("oscFader." + p + ".hotkeyMinus=" + OscHotkey.Format(b.HotkeyMinus));
			lines.Add("oscFader." + p + ".hotkeyPlus=" + OscHotkey.Format(b.HotkeyPlus));
		}
		for (int i = 0; i < toggles.Count; i++) {
			OscToggleBinding binding = toggles[i];
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".name=" + binding.Name.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".address=" + binding.Address.Trim());
			lines.Add("oscToggle." + i.ToString(CultureInfo.InvariantCulture) + ".hotkey=" + OscHotkey.Format(binding.Hotkey));
		}
		try {
			File.WriteAllText(ConfigPath, string.Join(Environment.NewLine, lines) + Environment.NewLine);
			LastDiskOutcome = AppConfigDiskOutcome.SavedOk;
			LastDiskFeedback = "Saved to disk.";
		} catch (Exception ex) {
			LastDiskOutcome = AppConfigDiskOutcome.SaveFailed;
			LastDiskFeedback = "Save failed: " + ex.Message;
		}
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
					row.Name = value.Trim();
					break;
				case "address":
					row.Address = value.Trim();
					break;
				case "step":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float st) && float.IsFinite(st))
						row.Step = st;
					break;
				case "minimum":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mn) && float.IsFinite(mn))
						row.Minimum = mn;
					break;
				case "maximum":
					if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float mx) && float.IsFinite(mx))
						row.Maximum = mx;
					break;
				case "hotkeyminus":
					if (OscHotkey.TryParse(value, out var hm))
						row.HotkeyMinus = hm;
					break;
				case "hotkeyplus":
					if (OscHotkey.TryParse(value, out var hp))
						row.HotkeyPlus = hp;
					break;
			}
		}

		var result = new List<OscFaderBinding>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			OscFaderBinding row = rows[index];
			if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Address))
				continue;
			if (!float.IsFinite(row.Step) || !float.IsFinite(row.Minimum) || !float.IsFinite(row.Maximum) || row.Minimum > row.Maximum)
				continue;
			row.Step = Math.Clamp(FaderFloatUtil.RoundToBindingDecimals(row.Step), MixerController.Config.MinFaderStep, MixerController.Config.MaxFaderStep);
			float minR = FaderFloatUtil.RoundToBindingDecimals(row.Minimum);
			float maxR = FaderFloatUtil.RoundToBindingDecimals(row.Maximum);
			result.Add(new OscFaderBinding {
				Name = row.Name.Trim(),
				Address = row.Address.Trim(),
				Step = row.Step,
				Minimum = minR,
				Maximum = maxR,
				HotkeyMinus = OscHotkey.Normalize(row.HotkeyMinus),
				HotkeyPlus = OscHotkey.Normalize(row.HotkeyPlus),
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
					row.Name = value.Trim();
					break;
				case "address":
					row.Address = value.Trim();
					break;
				case "hotkey":
					if (OscHotkey.TryParse(value, out var hotkey))
						row.Hotkey = hotkey;
					break;
			}
		}

		var result = new List<OscToggleBinding>(rows.Count);
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			OscToggleBinding row = rows[index];
			if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Address) || row.Hotkey == Keys.None)
				continue;
			result.Add(new OscToggleBinding {
				Name = row.Name.Trim(),
				Address = row.Address.Trim(),
				Hotkey = OscHotkey.Normalize(row.Hotkey),
			});
		}
		return result;
	}
}
