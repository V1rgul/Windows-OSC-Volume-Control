using System.Globalization;
using System.Net;
using System.Windows.Forms;

namespace X32VolumeHijacker;

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
	const string FileName = "X32VolumeHijacker.config";

	public string ConfigPath => Path.Combine(AppContext.BaseDirectory, FileName);

	/// <summary>Current aggregate; replaced by <see cref="LoadFromDisk"/>, <see cref="AdoptAppConfig"/>, or load fallbacks.</summary>
	public AppConfig AppConfig { get; private set; }

	public string LastDiskFeedback { get; private set; } = "";

	public AppConfigDiskOutcome LastDiskOutcome { get; private set; } = AppConfigDiskOutcome.None;

	public ConfigStore() {
		AppConfig = new AppConfig();
	}

	/// <summary>Replaces <see cref="AppConfig"/> with a deep copy of <paramref name="fromForm"/> (before apply and before disk).</summary>
	public void AdoptAppConfig(AppConfig fromForm) {
		ArgumentNullException.ThrowIfNull(fromForm);
		AppConfig = fromForm.DeepClone();
	}

	/// <summary>Reads the config file or sets defaults; always assigns <see cref="AppConfig"/>.</summary>
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
			    || !map.TryGetValue("faderAddress", out string? faderRaw)
			    || !OscConnectionConfigParse.TryParse(ipStr, portStr, faderRaw, out IPAddress ip, out int port, out string fader, out _, out _, out _)) {
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
			var faderDefaults = new MixerController.Config();
			float volumeStep = faderDefaults.VolumeStep;
			if (map.TryGetValue("volumeStep", out string? stepStr)
			    && float.TryParse(stepStr.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float vs)
			    && float.IsFinite(vs))
				volumeStep = Math.Clamp(vs, MixerController.Config.MinVolumeStep, MixerController.Config.MaxVolumeStep);
			uint faderCacheTtlMs = faderDefaults.FaderVolumeCacheTtlMs;
			if (map.TryGetValue("faderVolumeCacheTtlMs", out string? ttlStr)
			    && uint.TryParse(ttlStr.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint ttl))
				faderCacheTtlMs = Math.Min(ttl, MixerController.Config.MaxFaderVolumeCacheTtlMs);
			List<OscToggleBinding> oscToggles = ParseOscToggleBindings(map);
			AppConfig = new AppConfig {
				OscController = new OscController.Config {
					EndPoint = new IPEndPoint(ip, port),
					faderAddress = fader,
					timeoutMs = timeout,
				},
				Mixer = new MixerController.Config {
					VolumeStep = volumeStep,
					FaderVolumeCacheTtlMs = faderCacheTtlMs,
				},
				TrayApp = new TrayApp.Config { Bindings = oscToggles },
			};
			LastDiskOutcome = AppConfigDiskOutcome.LoadedOk;
			LastDiskFeedback = "Loaded settings from disk.";
		} catch (Exception ex) {
			AppConfig = new AppConfig();
			LastDiskOutcome = AppConfigDiskOutcome.LoadIoError;
			LastDiskFeedback = "Could not read config file: " + ex.Message;
		}
	}

	/// <summary>Writes <see cref="AppConfig"/> to disk. On failure sets <see cref="AppConfigDiskOutcome.SaveFailed"/> without throwing or reverting <see cref="AppConfig"/>.</summary>
	public void TryPersistToDisk() {
		AppConfig cfg = AppConfig;
		var osc = cfg.OscController;
		var faderConfig = cfg.Mixer ?? new MixerController.Config();
		var toggles = cfg.TrayApp?.Bindings ?? [];
		string fader = (osc.faderAddress ?? "").Trim();
		var lines = new List<string> {
			"ip=" + osc.EndPoint.Address.ToString(),
			"port=" + osc.EndPoint.Port.ToString(CultureInfo.InvariantCulture),
			"faderAddress=" + fader,
			"timeoutMs=" + osc.timeoutMs.ToString(CultureInfo.InvariantCulture),
			"volumeStep=" + faderConfig.VolumeStep.ToString(CultureInfo.InvariantCulture),
			"faderVolumeCacheTtlMs=" + faderConfig.FaderVolumeCacheTtlMs.ToString(CultureInfo.InvariantCulture),
		};
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
