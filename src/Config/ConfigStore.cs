using System.Globalization;
using System.IO;
using System.Net;
using Result;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.Config;

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
	const string APP_DIRECTORY_NAME = "Windows-OSC-Volume-Control";

	static string configPathTail => Path.Combine(APP_DIRECTORY_NAME, FILE_NAME);

	public string configPath => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
		configPathTail);

	public string configPathForUi => Path.Combine("%APPDATA%", configPathTail);

	public AppConfig appConfig { get; private set; }

	public string lastDiskFeedback { get; private set; } = "";

	public AppConfigDiskOutcome lastDiskOutcome { get; private set; } = AppConfigDiskOutcome.NONE;

	public UiTextFeedback lastDiskUiFeedback => new(lastDiskFeedback, diskUiKind(lastDiskOutcome));

	public static UiTextFeedback reloadSettingsSuccessFeedback() =>
		new("Reloaded settings from disk.", UiTextFeedbackKind.SUCCESS);

	public static UiTextFeedback explorerLaunchFailedFeedback(Exception ex) =>
		new(ex.Message, UiTextFeedbackKind.ERROR);

	static UiTextFeedbackKind diskUiKind(AppConfigDiskOutcome o) => o switch {
		AppConfigDiskOutcome.LOAD_IO_ERROR or AppConfigDiskOutcome.SAVE_FAILED => UiTextFeedbackKind.ERROR,
		AppConfigDiskOutcome.LOADED_PARTIAL => UiTextFeedbackKind.WARNING,
		AppConfigDiskOutcome.SAVED_OK or AppConfigDiskOutcome.LOADED_OK => UiTextFeedbackKind.SUCCESS,
		_ => UiTextFeedbackKind.DEFAULT,
	};

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
			IReadOnlyDictionary<string, string> map = ConfigParseUtil.parseKeyValueLines(File.ReadAllText(path));
			var repairNotes = new List<string>();
			OscTransport.Config osc = buildOscConfigFromMap(map, repairNotes);
			MixerController.Config mixer = buildMixerConfigFromMap(map, repairNotes);
			BindingManager.Config bindingConfig = buildBindingManagerConfigFromMap(map, repairNotes);
			KeyboardHook.Config keyboardHook = buildKeyboardHookConfigFromMap(map, repairNotes);
			appConfig = new AppConfig {
				oscTransport = osc,
				mixer = mixer,
				trayApp = bindingConfig,
				keyboardHook = keyboardHook,
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
		BindingManager.Config tray = cfg.trayApp;
		List<BindingAbstract> bindings = tray.bindings;
		KeyboardHook.Config hk = KeyboardHook.Config.Clamped(cfg.keyboardHook);
		var lines = new List<string> {
			"configVersion=1",
			"ip=" + osc.endPoint.Address,
			"port=" + osc.endPoint.Port.ToString(CultureInfo.InvariantCulture),
			"timeoutMs=" + mixer.timeoutMs.ToString(CultureInfo.InvariantCulture),
			"valueCacheTtlMs=" + mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture),
			"osdHeightDip=" + osd.heightDip.ToString(CultureInfo.InvariantCulture),
			"osdDisplayDurationMs=" + osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture),
			"osdScreenAnchor=" + osd.screenAnchor.ToString(),
			"hotkeyLongPressMs=" + hk.longPressDurationMs.ToString(CultureInfo.InvariantCulture),
			"hotkeyOptimizeNonLongPressKeyDown=" + (hk.optimizeNonLongPressKeyDown ? "true" : "false"),
			"hotkeySuppressKeyForLongPressOnly=" + (hk.suppressKeyForLongPressOnlyGestures ? "true" : "false"),
			"hotkeyAcceptMacroChordKeyOrder=" + (hk.acceptMacroChordKeyOrder ? "true" : "false"),
		};
		for (int i = 0; i < bindings.Count; i++) {
			BindingAbstract b = bindings[i];
			string p = i.ToString(CultureInfo.InvariantCulture);
			lines.Add("osc." + p + ".name=" + sanitizeConfigValue(b.name));
			lines.Add("osc." + p + ".address=" + sanitizeConfigValue(b.address));
			switch (b) {
				case BindingLinear f:
					lines.Add("osc." + p + ".type=linear");
					lines.Add("osc." + p + ".minimum=" + ContinuousFloatUtil.formatFloatForConfig(f.minimum, f.minimumFractionalDigits));
					lines.Add("osc." + p + ".maximum=" + ContinuousFloatUtil.formatFloatForConfig(f.maximum, f.maximumFractionalDigits));
					if (f.unit is { } lu)
						lines.Add("osc." + p + ".unit=" + sanitizeConfigValue(lu));
					break;
				case BindingLinf lf: {
					lines.Add("osc." + p + ".type=linf");
					lines.Add("osc." + p + ".minimum=" + ContinuousFloatUtil.formatFloatForConfig(lf.minimum, lf.minimumFractionalDigits));
					lines.Add("osc." + p + ".maximum=" + ContinuousFloatUtil.formatFloatForConfig(lf.maximum, lf.maximumFractionalDigits));
					int rMinDig = ContinuousFloatUtil.fractionalDigitsForValue(lf.rangeMinimum);
					int rMaxDig = ContinuousFloatUtil.fractionalDigitsForValue(lf.rangeMaximum);
					lines.Add("osc." + p + ".rangeMinimum=" + ContinuousFloatUtil.formatFloatForConfig(lf.rangeMinimum, rMinDig));
					lines.Add("osc." + p + ".rangeMaximum=" + ContinuousFloatUtil.formatFloatForConfig(lf.rangeMaximum, rMaxDig));
					if (lf.unit is { } lu2)
						lines.Add("osc." + p + ".unit=" + sanitizeConfigValue(lu2));
					break;
				}
				case BindingLogf lg: {
					lines.Add("osc." + p + ".type=logf");
					lines.Add("osc." + p + ".minimum=" + ContinuousFloatUtil.formatFloatForConfig(lg.minimum, lg.minimumFractionalDigits));
					lines.Add("osc." + p + ".maximum=" + ContinuousFloatUtil.formatFloatForConfig(lg.maximum, lg.maximumFractionalDigits));
					int gRMinDig = ContinuousFloatUtil.fractionalDigitsForValue(lg.rangeMinimum);
					int gRMaxDig = ContinuousFloatUtil.fractionalDigitsForValue(lg.rangeMaximum);
					lines.Add("osc." + p + ".rangeMinimum=" + ContinuousFloatUtil.formatFloatForConfig(lg.rangeMinimum, gRMinDig));
					lines.Add("osc." + p + ".rangeMaximum=" + ContinuousFloatUtil.formatFloatForConfig(lg.rangeMaximum, gRMaxDig));
					if (lg.unit is { } lu3)
						lines.Add("osc." + p + ".unit=" + sanitizeConfigValue(lu3));
					break;
				}
				case BindingLevel lv:
					lines.Add("osc." + p + ".type=level");
					lines.Add("osc." + p + ".minimum=" + ContinuousFloatUtil.formatFloatForConfig(lv.minimum, lv.minimumFractionalDigits));
					lines.Add("osc." + p + ".maximum=" + ContinuousFloatUtil.formatFloatForConfig(lv.maximum, lv.maximumFractionalDigits));
					break;
				case BindingToggle:
					lines.Add("osc." + p + ".type=toggle");
					break;
			}

			for (int h = 0; h < b.actions.Count; h++) {
				ControlAction ha = b.actions[h];
				string hp = h.ToString(CultureInfo.InvariantCulture);
				lines.Add("osc." + p + ".hotkey." + hp + ".key=" + HotkeyUtil.format(ha.hotkey));
				if (ha.longPress)
					lines.Add("osc." + p + ".hotkey." + hp + ".longPress=true");
				switch (ha) {
					case ControlActionContinuousSet fs:
						lines.Add("osc." + p + ".hotkey." + hp + ".action=set");
						lines.Add("osc." + p + ".hotkey." + hp + ".value=" + ContinuousFloatUtil.formatFloatForConfig(fs.value, fs.fractionalDigits));
						break;
					case ControlActionContinuousDelta fd:
						lines.Add("osc." + p + ".hotkey." + hp + ".action=delta");
						lines.Add("osc." + p + ".hotkey." + hp + ".value=" + ContinuousFloatUtil.formatFloatForConfig(fd.delta, fd.fractionalDigits));
						break;
					case ControlActionContinuousRawDelta rd:
						lines.Add("osc." + p + ".hotkey." + hp + ".action=raw_delta");
						lines.Add("osc." + p + ".hotkey." + hp + ".value=" + ContinuousFloatUtil.formatFloatForConfig(rd.delta, rd.fractionalDigits));
						break;
					case ControlActionToggleSet ts:
						lines.Add("osc." + p + ".hotkey." + hp + ".action=set");
						lines.Add("osc." + p + ".hotkey." + hp + ".value=" + (ts.on ? "true" : "false"));
						break;
					case ControlActionToggleFlip:
						lines.Add("osc." + p + ".hotkey." + hp + ".action=toggle");
						break;
				}
			}
		}
		string path = configPath;
		string? directory = Path.GetDirectoryName(path);
		string tmpPath = path + ".tmp";
		try {
			if (!string.IsNullOrWhiteSpace(directory))
				Directory.CreateDirectory(directory);
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

	/// <summary>Free-text values are stored in a line-oriented key=value file: line breaks would corrupt or inject keys, so they are flattened on save.</summary>
	static string sanitizeConfigValue(string value) =>
		value.Trim().Replace('\r', ' ').Replace('\n', ' ');

	static List<BindingAbstract> cloneDefaultBindings() {
		var trayDefaults = new BindingManager.Config();
		return trayDefaults.bindings.Select(BindingManager.cloneBinding).ToList();
	}

	static BindingManager.Config buildBindingManagerConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		List<BindingAbstract> list = parseOscBindings(map, repairNotes);
		if (list.Count == 0) {
			list = cloneDefaultBindings();
			repairNotes.Add("No valid OSC bindings in file; using defaults.");
		}
		return new BindingManager.Config { bindings = list };
	}

	static KeyboardHook.Config buildKeyboardHookConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var provisional = new KeyboardHook.Config();
		if (map.TryGetValue("hotkeyLongPressMs", out string? lpStr)) {
			KeyboardHook.Config.parseLongPressMs(lpStr).@switch(
				v => provisional.longPressDurationMs = v,
				_ => repairNotes.Add("hotkeyLongPressMs invalid; using default."));
		}
		if (map.TryGetValue("hotkeyOptimizeNonLongPressKeyDown", out string? optStr)) {
			if (tryParseBoolLoose(optStr, out bool opt))
				provisional.optimizeNonLongPressKeyDown = opt;
			else
				repairNotes.Add("hotkeyOptimizeNonLongPressKeyDown invalid; using default.");
		}
		if (map.TryGetValue("hotkeySuppressKeyForLongPressOnly", out string? supStr)) {
			if (tryParseBoolLoose(supStr, out bool sup))
				provisional.suppressKeyForLongPressOnlyGestures = sup;
			else
				repairNotes.Add("hotkeySuppressKeyForLongPressOnly invalid; using default.");
		}
		if (map.TryGetValue("hotkeyAcceptMacroChordKeyOrder", out string? macroStr)) {
			if (tryParseBoolLoose(macroStr, out bool macro))
				provisional.acceptMacroChordKeyOrder = macro;
			else
				repairNotes.Add("hotkeyAcceptMacroChordKeyOrder invalid; using default.");
		}
		return provisional;
	}

	static OscTransport.Config buildOscConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		map.TryGetValue("ip", out string? ipStr);
		map.TryGetValue("port", out string? portStr);
		Result<IPAddress> ip = OscTransport.Config.parseIpField(ipStr);
		Result<int> port = OscTransport.Config.parsePortField(portStr);
		if (ip.isSuccess && port.isSuccess)
			return new OscTransport.Config { endPoint = new IPEndPoint(ip.value, port.value) };
		repairNotes.Add("OSC IP/port missing or invalid; using connection defaults.");
		return new OscTransport.Config();
	}

	static MixerController.Config buildMixerConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var mixerDefaults = new MixerController.Config();
		uint timeoutMs = mixerDefaults.timeoutMs;
		if (map.TryGetValue("timeoutMs", out string? toStr)) {
			MixerController.Config.parseTimeoutMs(toStr).@switch(
				v => timeoutMs = v,
				_ => repairNotes.Add("timeoutMs invalid; using default."));
		}

		uint valueCacheTtlMs = mixerDefaults.ValueCacheTtlMs;
		if (map.TryGetValue("valueCacheTtlMs", out string? ttlStr)) {
			MixerController.Config.parseValueCacheTtlMs(ttlStr).@switch(
				v => valueCacheTtlMs = v,
				_ => repairNotes.Add("valueCacheTtlMs invalid; using default."));
		}
		return new MixerController.Config {
			timeoutMs = timeoutMs,
			ValueCacheTtlMs = valueCacheTtlMs,
		};
	}

	static OSDController.Config buildOsdConfigFromMap(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var o = new OSDController.Config();
		if (map.TryGetValue("osdHeightDip", out string? hStr)) {
			OSDController.Config.parseHeightDip(hStr).@switch(
				v => o.heightDip = v,
				_ => repairNotes.Add("osdHeightDip invalid; using default."));
		}
		if (map.TryGetValue("osdDisplayDurationMs", out string? dStr)) {
			OSDController.Config.parseDisplayDurationMs(dStr).@switch(
				v => o.DisplayDurationMs = v,
				_ => repairNotes.Add("osdDisplayDurationMs invalid; using default."));
		}
		if (map.TryGetValue("osdScreenAnchor", out string? anchorStr)) {
			if (!Enum.TryParse(anchorStr.Trim(), ignoreCase: true, out OSDController.Config.OsdScreenAnchor parsed) || !Enum.IsDefined(parsed))
				repairNotes.Add("osdScreenAnchor invalid; ignored.");
			else
				o.screenAnchor = parsed;
		}
		return o;
	}

	sealed class OscBindingLoadRow {
		public string name = "";
		public string address = "";
		public string type = "";
		public string minimumRaw = "";
		public string maximumRaw = "";
		public float minimum;
		public float maximum;
		public float rangeMinimum = float.NaN;
		public float rangeMaximum = float.NaN;
		public string? unitRaw;
		public readonly Dictionary<int, OscHotkeyLoadRow> hotkeyRows = new();
	}

	sealed class OscHotkeyLoadRow {
		public string keyText = "";
		public string action = "";
		public string valueText = "";
		public bool longPress;
	}

	static bool tryParseOscBindingKey(string key, out int bindingIndex, out string remainder) {
		remainder = "";
		bindingIndex = -1;
		if (!key.StartsWith("osc.", StringComparison.OrdinalIgnoreCase))
			return false;
		string rest = key["osc.".Length..];
		int dot = rest.IndexOf('.');
		if (dot <= 0)
			return false;
		if (!int.TryParse(rest[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0)
			return false;
		bindingIndex = idx;
		remainder = rest[(dot + 1)..];
		return true;
	}

	static List<BindingAbstract> parseOscBindings(IReadOnlyDictionary<string, string> map, List<string> repairNotes) {
		var rows = new Dictionary<int, OscBindingLoadRow>();
		foreach ((string key, string value) in map) {
			if (!tryParseOscBindingKey(key, out int bi, out string rem))
				continue;
			if (!rows.TryGetValue(bi, out OscBindingLoadRow? row)) {
				row = new OscBindingLoadRow();
				rows[bi] = row;
			}
			if (rem.StartsWith("hotkey.", StringComparison.OrdinalIgnoreCase)) {
				string hkRest = rem["hotkey.".Length..];
				int dot = hkRest.IndexOf('.');
				if (dot <= 0)
					continue;
				if (!int.TryParse(hkRest[..dot], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hj) || hj < 0)
					continue;
				string field = hkRest[(dot + 1)..];
				if (!row.hotkeyRows.TryGetValue(hj, out OscHotkeyLoadRow? hk))
					row.hotkeyRows[hj] = hk = new OscHotkeyLoadRow();
				switch (field.ToLowerInvariant()) {
					case "key":
						hk.keyText = value.Trim();
						break;
					case "action":
						hk.action = value.Trim();
						break;
					case "value":
						hk.valueText = value.Trim();
						break;
					case "longpress":
						if (tryParseBoolLoose(value, out bool lp))
							hk.longPress = lp;
						break;
				}
			} else {
				switch (rem.ToLowerInvariant()) {
					case "name":
						row.name = value.Trim();
						break;
					case "address":
						row.address = value.Trim();
						break;
					case "type":
						row.type = value.Trim();
						break;
					case "minimum":
						row.minimumRaw = value.Trim();
						if (float.TryParse(row.minimumRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float mn) && float.IsFinite(mn))
							row.minimum = mn;
						break;
					case "maximum":
						row.maximumRaw = value.Trim();
						if (float.TryParse(row.maximumRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out float mx) && float.IsFinite(mx))
							row.maximum = mx;
						break;
					case "rangeminimum":
						if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rmn) && float.IsFinite(rmn))
							row.rangeMinimum = rmn;
						break;
					case "rangemaximum":
						if (float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rmx) && float.IsFinite(rmx))
							row.rangeMaximum = rmx;
						break;
					case "unit":
						row.unitRaw = value.Trim();
						break;
				}
			}
		}

		var result = new List<BindingAbstract>();
		foreach (int index in rows.Keys.OrderBy(i => i)) {
			OscBindingLoadRow r = rows[index];
			if (string.IsNullOrWhiteSpace(r.name) || string.IsNullOrWhiteSpace(r.address))
				continue;
			string t = r.type.Trim().ToLowerInvariant();
			if (t is not ("linear" or "toggle" or "linf" or "logf" or "level"))
				continue;

			var actions = new List<ControlAction>();
			foreach (int hj in r.hotkeyRows.Keys.OrderBy(j => j)) {
				OscHotkeyLoadRow hk = r.hotkeyRows[hj];
				if (!HotkeyUtil.tryParse(hk.keyText, out HotkeyGesture gesture))
					continue;
				gesture = HotkeyUtil.normalize(gesture);
				if (gesture.isNone)
					continue;
				ControlAction? action = tryBuildControlActionFromFile(t, hk.action, hk.valueText);
				if (action == null)
					continue;
				action.hotkey = gesture;
				action.longPress = hk.longPress;
				actions.Add(action);
			}

			int minDigits = ContinuousFloatUtil.fractionalDigitsOfTypedString(r.minimumRaw);
			int maxDigits = ContinuousFloatUtil.fractionalDigitsOfTypedString(r.maximumRaw);

			switch (t) {
				case "linear":
					if (!float.IsFinite(r.minimum) || !float.IsFinite(r.maximum) || r.minimum > r.maximum)
						continue;
					result.Add(new BindingLinear {
						name = r.name.Trim(),
						address = r.address.Trim(),
						minimum = ContinuousFloatUtil.RoundToBindingDecimals(r.minimum),
						maximum = ContinuousFloatUtil.RoundToBindingDecimals(r.maximum),
						minimumFractionalDigits = minDigits,
						maximumFractionalDigits = maxDigits,
						unit = string.IsNullOrWhiteSpace(r.unitRaw) ? null : r.unitRaw,
						actions = actions,
					});
					break;
				case "linf":
					if (!float.IsFinite(r.minimum) || !float.IsFinite(r.maximum) || r.minimum > r.maximum)
						continue;
					float linfLimitMin = ContinuousFloatUtil.RoundToBindingDecimals(r.minimum);
					float linfLimitMax = ContinuousFloatUtil.RoundToBindingDecimals(r.maximum);
					float linfRangeMin = float.IsFinite(r.rangeMinimum) ? ContinuousFloatUtil.RoundToBindingDecimals(r.rangeMinimum) : linfLimitMin;
					float linfRangeMax = float.IsFinite(r.rangeMaximum) ? ContinuousFloatUtil.RoundToBindingDecimals(r.rangeMaximum) : linfLimitMax;
					if (linfRangeMin > linfRangeMax) {
						repairNotes.Add($"Binding \"{r.name}\": invalid rangeMinimum/rangeMaximum; using minimum/maximum.");
						linfRangeMin = linfLimitMin;
						linfRangeMax = linfLimitMax;
					}
					result.Add(new BindingLinf {
						name = r.name.Trim(),
						address = r.address.Trim(),
						minimum = linfLimitMin,
						maximum = linfLimitMax,
						rangeMinimum = linfRangeMin,
						rangeMaximum = linfRangeMax,
						minimumFractionalDigits = minDigits,
						maximumFractionalDigits = maxDigits,
						unit = string.IsNullOrWhiteSpace(r.unitRaw) ? null : r.unitRaw,
						actions = actions,
					});
					break;
				case "logf":
					if (!float.IsFinite(r.minimum) || !float.IsFinite(r.maximum) || r.minimum > r.maximum || r.minimum <= 0f || r.maximum <= 0f)
						continue;
					float logfLimitMin = ContinuousFloatUtil.RoundToBindingDecimals(r.minimum);
					float logfLimitMax = ContinuousFloatUtil.RoundToBindingDecimals(r.maximum);
					float logfRangeMin = float.IsFinite(r.rangeMinimum) ? ContinuousFloatUtil.RoundToBindingDecimals(r.rangeMinimum) : logfLimitMin;
					float logfRangeMax = float.IsFinite(r.rangeMaximum) ? ContinuousFloatUtil.RoundToBindingDecimals(r.rangeMaximum) : logfLimitMax;
					if (logfRangeMin > logfRangeMax || logfRangeMin <= 0f || logfRangeMax <= 0f) {
						repairNotes.Add($"Binding \"{r.name}\": invalid rangeMinimum/rangeMaximum for logf; using minimum/maximum.");
						logfRangeMin = logfLimitMin;
						logfRangeMax = logfLimitMax;
					}
					result.Add(new BindingLogf {
						name = r.name.Trim(),
						address = r.address.Trim(),
						minimum = logfLimitMin,
						maximum = logfLimitMax,
						rangeMinimum = logfRangeMin,
						rangeMaximum = logfRangeMax,
						minimumFractionalDigits = minDigits,
						maximumFractionalDigits = maxDigits,
						unit = string.IsNullOrWhiteSpace(r.unitRaw) ? null : r.unitRaw,
						actions = actions,
					});
					break;
				case "level":
					if (!float.IsFinite(r.minimum) || !float.IsFinite(r.maximum) || r.minimum > r.maximum)
						continue;
					result.Add(new BindingLevel {
						name = r.name.Trim(),
						address = r.address.Trim(),
						minimum = ContinuousFloatUtil.RoundToBindingDecimals(r.minimum),
						maximum = ContinuousFloatUtil.RoundToBindingDecimals(r.maximum),
						minimumFractionalDigits = minDigits,
						maximumFractionalDigits = maxDigits,
						actions = actions,
					});
					break;
				case "toggle":
					result.Add(new BindingToggle {
						name = r.name.Trim(),
						address = r.address.Trim(),
						actions = actions,
					});
					break;
			}
		}
		return result;
	}

	static ControlAction? tryBuildControlActionFromFile(string bindingType, string actionToken, string valueText) {
		string a = actionToken.Trim().ToLowerInvariant();
		int frac = ContinuousFloatUtil.fractionalDigitsOfTypedString(valueText);
		bool isFloatBinding = bindingType is "linear" or "linf" or "logf" or "level";
		if (isFloatBinding) {
			if (a == "set" && float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float sv) && float.IsFinite(sv))
				return new ControlActionContinuousSet {
					value = ContinuousFloatUtil.RoundToBindingDecimals(sv),
					fractionalDigits = frac,
				};
			if (a == "delta" && float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float dv) && float.IsFinite(dv))
				return new ControlActionContinuousDelta {
					delta = ContinuousFloatUtil.RoundToBindingDecimals(dv),
					fractionalDigits = frac,
				};
			if (a == "raw_delta" && float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float rv) && float.IsFinite(rv))
				return new ControlActionContinuousRawDelta {
					delta = ContinuousFloatUtil.RoundToBindingDecimals(rv),
					fractionalDigits = frac,
				};
			return null;
		}
		if (bindingType == "toggle") {
			if (a == "toggle")
				return new ControlActionToggleFlip();
			if (a == "set" && tryParseBoolLoose(valueText, out bool on))
				return new ControlActionToggleSet { on = on };
		}
		return null;
	}

	internal static AppConfig loadAppConfigFromKeyValueTextForTests(string raw, out List<string> repairNotes) {
		repairNotes = new List<string>();
		IReadOnlyDictionary<string, string> map = ConfigParseUtil.parseKeyValueLines(raw);
		return new AppConfig {
			oscTransport = buildOscConfigFromMap(map, repairNotes),
			mixer = buildMixerConfigFromMap(map, repairNotes),
			trayApp = buildBindingManagerConfigFromMap(map, repairNotes),
			keyboardHook = buildKeyboardHookConfigFromMap(map, repairNotes),
			osd = buildOsdConfigFromMap(map, repairNotes),
		};
	}

	static bool tryParseBoolLoose(string text, out bool on) {
		on = false;
		string s = text.Trim();
		if (bool.TryParse(s, out on))
			return true;
		if (s == "1") {
			on = true;
			return true;
		}
		if (s == "0") {
			on = false;
			return true;
		}
		if (s.Equals("on", StringComparison.OrdinalIgnoreCase)) {
			on = true;
			return true;
		}
		if (s.Equals("off", StringComparison.OrdinalIgnoreCase)) {
			on = false;
			return true;
		}
		return false;
	}
}
