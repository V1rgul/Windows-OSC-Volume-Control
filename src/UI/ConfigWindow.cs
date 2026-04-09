using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WindowsOscVolumeControl;

public partial class ConfigWindow : Window {
	readonly MixerController _mixer;
	readonly TrayController _trayController;
	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;

	public ObservableCollection<FaderBindingEditor> FaderBindings { get; } = [];
	public ObservableCollection<ToggleBindingEditor> ToggleBindings { get; } = [];

	public ConfigWindow(MixerController mixer, TrayController trayController, AppCoordinator appCoordinator, ConfigStore configStore) {
		InitializeComponent();
		DataContext = this;
		_mixer = mixer;
		_trayController = trayController;
		_appCoordinator = appCoordinator;
		_configStore = configStore;
		loadFromConfigStore();
		syncTitlebarIconFromTray();
		refreshAutostartFeedback();
	}

	public void syncTitlebarIconFromTray() => Icon = _trayController.windowIconSourceSnapshot;

	void loadFromConfigStore() {
		AppConfig cfg = _configStore.appConfig;
		IpTextBox.Text = cfg.oscTransport.endPoint.Address.ToString();
		PortTextBox.Text = cfg.oscTransport.endPoint.Port.ToString(CultureInfo.InvariantCulture);
		TimeoutTextBox.Text = cfg.mixer.timeoutMs.ToString(CultureInfo.InvariantCulture);
		CacheTtlTextBox.Text = cfg.mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture);
		OsdHeightTextBox.Text = cfg.osd.HeightPx.ToString(CultureInfo.InvariantCulture);
		OsdDurationTextBox.Text = cfg.osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture);
		ConfigPathTextBox.Text = _configStore.configPath;
		ConfigFeedbackTextBlock.Text = _configStore.lastDiskFeedback;
		DiskFeedbackTextBlock.Text = _configStore.lastDiskFeedback;
		InfoResultTextBox.Text = "";
		NetworkFeedbackTextBlock.Text = "";
		StatusTextBlock.Text = "";
		FaderBindings.Clear();
		foreach (BindingFader binding in cfg.trayApp?.faderBindings ?? [])
			FaderBindings.Add(FaderBindingEditor.fromBinding(binding));
		ToggleBindings.Clear();
		foreach (BindingToggle binding in cfg.trayApp?.bindings ?? [])
			ToggleBindings.Add(ToggleBindingEditor.fromBinding(binding));
	}

	async void buttonApplySaveAndTest_Click(object sender, RoutedEventArgs e) {
		StatusTextBlock.Foreground = Brushes.DarkOrange;
		StatusTextBlock.Text = "";
		if (!tryBuildConfig(out AppConfig? newConfig, out string? error)) {
			StatusTextBlock.Foreground = Brushes.IndianRed;
			StatusTextBlock.Text = error ?? "Invalid configuration.";
			return;
		}

		_appCoordinator.beginConfigValidation();
		try {
			_appCoordinator.commitConfigFromSettingsForm(newConfig);
			ConfigFeedbackTextBlock.Text = _configStore.lastDiskFeedback;
			DiskFeedbackTextBlock.Text = _configStore.lastDiskFeedback;

			var progress = new Progress<(string text, FeedbackTone tone)>(sample => {
				NetworkFeedbackTextBlock.Text = sample.text;
				NetworkFeedbackTextBlock.Foreground = brushForTone(sample.tone);
			});

			int timeoutMs = Math.Max(1, (int)newConfig.mixer.timeoutMs);
			Task<(string text, FeedbackTone tone)> pingTask = NetworkPingTest.PingFeedbackAsync(newConfig.oscTransport.endPoint.Address, timeoutMs: timeoutMs, probeProgress: progress);
			Task<(bool Ok, string Detail)> infoTask = _mixer.QueryInfoAsync();
			await Task.WhenAll(pingTask, infoTask);

			(string pingText, FeedbackTone pingTone) = pingTask.Result;
			(bool ok, string detail) = infoTask.Result;
			NetworkFeedbackTextBlock.Text = pingText;
			NetworkFeedbackTextBlock.Foreground = brushForTone(pingTone);
			InfoResultTextBox.Text = detail;
			StatusTextBlock.Foreground = ok ? Brushes.DarkGreen : Brushes.IndianRed;
			StatusTextBlock.Text = ok ? "Settings applied and mixer responded." : "Settings saved, but the mixer did not respond cleanly.";
		} catch (Exception ex) {
			StatusTextBlock.Foreground = Brushes.IndianRed;
			StatusTextBlock.Text = ex.Message;
		} finally {
			_appCoordinator.finishConfigValidation();
		}
	}

	void buttonReload_Click(object sender, RoutedEventArgs e) {
		_configStore.loadFromDisk();
		_appCoordinator.applyConfigFromStore();
		loadFromConfigStore();
		refreshAutostartFeedback();
		StatusTextBlock.Foreground = Brushes.DarkGreen;
		StatusTextBlock.Text = "Reloaded settings from disk.";
	}

	void buttonOpenConfigFolder_Click(object sender, RoutedEventArgs e) {
		string? dir = Path.GetDirectoryName(_configStore.configPath);
		if (string.IsNullOrEmpty(dir))
			return;
		try {
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = "\"" + dir + "\"",
				UseShellExecute = true,
			});
		} catch (Exception ex) {
			StatusTextBlock.Foreground = Brushes.IndianRed;
			StatusTextBlock.Text = ex.Message;
		}
	}

	void buttonRegisterAutostart_Click(object sender, RoutedEventArgs e) {
		if (WindowsAutostart.TryRegister(out string? error)) {
			refreshAutostartFeedback("Autostart registered.");
			return;
		}
		refreshAutostartFeedback(error ?? "Could not register autostart.");
	}

	void buttonDeregisterAutostart_Click(object sender, RoutedEventArgs e) {
		if (WindowsAutostart.TryDeregister(out string? error)) {
			refreshAutostartFeedback("Autostart removed.");
			return;
		}
		refreshAutostartFeedback(error ?? "Could not deregister autostart.");
	}

	void refreshAutostartFeedback(string? overrideText = null) {
		if (!string.IsNullOrWhiteSpace(overrideText)) {
			AutostartFeedbackTextBlock.Text = overrideText;
			return;
		}
		AutostartFeedbackTextBlock.Text = WindowsAutostart.IsRegistered()
			? "Autostart is currently registered."
			: "Autostart is currently not registered.";
	}

	void buttonAddFader_Click(object sender, RoutedEventArgs e) => FaderBindings.Add(new FaderBindingEditor());
	void buttonRemoveFader_Click(object sender, RoutedEventArgs e) => removeItem<FaderBindingEditor>(sender, FaderBindings);
	void buttonAddToggle_Click(object sender, RoutedEventArgs e) => ToggleBindings.Add(new ToggleBindingEditor());
	void buttonRemoveToggle_Click(object sender, RoutedEventArgs e) => removeItem<ToggleBindingEditor>(sender, ToggleBindings);

	static void removeItem<T>(object sender, Collection<T> collection) {
		if (sender is FrameworkElement { DataContext: T item })
			collection.Remove(item);
	}

	void buttonClearFaderMinus_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: FaderBindingEditor item })
			item.hotkeyMinus = HotkeyGesture.None;
	}

	void buttonClearFaderPlus_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: FaderBindingEditor item })
			item.hotkeyPlus = HotkeyGesture.None;
	}

	void buttonClearToggleHotkey_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: ToggleBindingEditor item })
			item.hotkey = HotkeyGesture.None;
	}

	void hotkeyControl_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
		_appCoordinator.setConfiguredHotkeysEnabled(false);

	void hotkeyControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
		_appCoordinator.setConfiguredHotkeysEnabled(true);

	void faderHotkeyMinus_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement { DataContext: FaderBindingEditor item })
			applyHotkey(e, hotkey => item.hotkeyMinus = hotkey, () => item.hotkeyMinus = HotkeyGesture.None);
	}

	void faderHotkeyPlus_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement { DataContext: FaderBindingEditor item })
			applyHotkey(e, hotkey => item.hotkeyPlus = hotkey, () => item.hotkeyPlus = HotkeyGesture.None);
	}

	void toggleHotkey_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement { DataContext: ToggleBindingEditor item })
			applyHotkey(e, hotkey => item.hotkey = hotkey, () => item.hotkey = HotkeyGesture.None);
	}

	void applyHotkey(KeyEventArgs e, Action<HotkeyGesture> setter, Action clear) {
		if (e.Key is Key.Delete or Key.Back) {
			clear();
			e.Handled = true;
			return;
		}

		HotkeyGesture hotkey = HotkeyUtil.fromKeyEventArgs(e);
		if (hotkey.isNone) {
			e.Handled = true;
			return;
		}

		if (!HotkeyUtil.tryValidate(hotkey, out string error)) {
			StatusTextBlock.Foreground = Brushes.IndianRed;
			StatusTextBlock.Text = error;
			e.Handled = true;
			return;
		}

		setter(hotkey);
		StatusTextBlock.Text = "";
		e.Handled = true;
	}

	static Brush brushForTone(FeedbackTone tone) => tone switch {
		FeedbackTone.SUCCESS => Brushes.DarkGreen,
		FeedbackTone.WARNING => Brushes.DarkOrange,
		FeedbackTone.ERROR => Brushes.IndianRed,
		_ => Brushes.Black,
	};

	bool tryBuildConfig(out AppConfig config, out string? error) {
		config = new AppConfig();
		error = null;

		if (!OscConnectionConfigParse.tryParseIpPort(IpTextBox.Text, PortTextBox.Text, out IPAddress ip, out int port, out _, out string? oscError)) {
			error = oscError ?? "Invalid OSC IP/port.";
			return false;
		}

		if (!tryParseUInt(TimeoutTextBox.Text, MixerController.Config.MIN_TIMEOUT_MS, MixerController.Config.MAX_TIMEOUT_MS, "Query timeout", out uint timeout, out error))
			return false;
		if (!tryParseUInt(CacheTtlTextBox.Text, 0, MixerController.Config.MAX_VALUE_CACHE_TTL_MS, "Value cache TTL", out uint ttl, out error))
			return false;
		if (!tryParseInt(OsdHeightTextBox.Text, OSDController.Config.MIN_HEIGHT_PX, OSDController.Config.MAX_HEIGHT_PX, "OSD height", out int osdHeight, out error))
			return false;
		if (!tryParseUInt(OsdDurationTextBox.Text, OSDController.Config.MIN_DISPLAY_DURATION_MS, OSDController.Config.MAX_DISPLAY_DURATION_MS, "OSD display duration", out uint osdDuration, out error))
			return false;

		List<BindingFader> faders = new(FaderBindings.Count);
		for (int i = 0; i < FaderBindings.Count; i++) {
			FaderBindingEditor editor = FaderBindings[i];
			if (isFaderBlank(editor))
				continue;

			if (string.IsNullOrWhiteSpace(editor.name) || string.IsNullOrWhiteSpace(editor.address)) {
				error = $"Fader binding {i + 1} requires name and OSC address.";
				return false;
			}

			if (!tryParseFloat(editor.step, "Fader step", out float step, out error)
			    || !tryParseFloat(editor.minimum, "Fader minimum", out float min, out error)
			    || !tryParseFloat(editor.maximum, "Fader maximum", out float max, out error))
				return false;

			if (min > max) {
				error = $"Fader binding {i + 1}: minimum must be less than or equal to maximum.";
				return false;
			}

			if (!editor.hotkeyMinus.isNone && !HotkeyUtil.tryValidate(editor.hotkeyMinus, out string hotkeyMinusError)) {
				error = $"Fader binding {i + 1} hotkey −: {hotkeyMinusError}";
				return false;
			}
			if (!editor.hotkeyPlus.isNone && !HotkeyUtil.tryValidate(editor.hotkeyPlus, out string hotkeyPlusError)) {
				error = $"Fader binding {i + 1} hotkey +: {hotkeyPlusError}";
				return false;
			}

			faders.Add(new BindingFader {
				name = editor.name.Trim(),
				address = editor.address.Trim(),
				step = Math.Clamp(FaderFloatUtil.RoundToBindingDecimals(step), MixerController.Config.MIN_FADER_STEP, MixerController.Config.MAX_FADER_STEP),
				minimum = FaderFloatUtil.RoundToBindingDecimals(min),
				maximum = FaderFloatUtil.RoundToBindingDecimals(max),
				hotkeyMinus = HotkeyUtil.normalize(editor.hotkeyMinus),
				hotkeyPlus = HotkeyUtil.normalize(editor.hotkeyPlus),
			});
		}

		if (faders.Count == 0) {
			error = "Add at least one valid fader binding.";
			return false;
		}

		List<BindingToggle> toggles = new(ToggleBindings.Count);
		for (int i = 0; i < ToggleBindings.Count; i++) {
			ToggleBindingEditor editor = ToggleBindings[i];
			if (isToggleBlank(editor))
				continue;

			if (string.IsNullOrWhiteSpace(editor.name) || string.IsNullOrWhiteSpace(editor.address) || editor.hotkey.isNone) {
				error = $"Toggle binding {i + 1} requires name, OSC address, and hotkey.";
				return false;
			}

			if (!HotkeyUtil.tryValidate(editor.hotkey, out string toggleHotkeyError)) {
				error = $"Toggle binding {i + 1}: {toggleHotkeyError}";
				return false;
			}

			toggles.Add(new BindingToggle {
				name = editor.name.Trim(),
				address = editor.address.Trim(),
				hotkey = HotkeyUtil.normalize(editor.hotkey),
			});
		}

		if (!tryValidateHotkeysGlobally(faders, toggles, out error))
			return false;

		config = new AppConfig {
			oscTransport = new OscTransport.Config {
				endPoint = new IPEndPoint(ip, port),
			},
			mixer = new MixerController.Config {
				timeoutMs = timeout,
				ValueCacheTtlMs = ttl,
			},
			osd = new OSDController.Config {
				HeightPx = osdHeight,
				DisplayDurationMs = osdDuration,
			},
			trayApp = new BindingManager.Config {
				faderBindings = faders,
				bindings = toggles,
			},
		};
		return true;
	}

	static bool tryParseUInt(string text, uint min, uint max, string label, out uint value, out string? error) {
		value = 0;
		error = null;
		if (!uint.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed)) {
			error = $"{label} must be an integer.";
			return false;
		}
		if (parsed < min || parsed > max) {
			error = $"{label} must be between {min} and {max}.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool tryParseInt(string text, int min, int max, string label, out int value, out string? error) {
		value = 0;
		error = null;
		if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
			error = $"{label} must be an integer.";
			return false;
		}
		if (parsed < min || parsed > max) {
			error = $"{label} must be between {min} and {max}.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool tryParseFloat(string text, string label, out float value, out string? error) {
		value = 0;
		error = null;
		if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || !float.IsFinite(parsed)) {
			error = $"{label} must be a finite number.";
			return false;
		}
		value = parsed;
		return true;
	}

	static bool isFaderBlank(FaderBindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& string.IsNullOrWhiteSpace(editor.step)
		&& string.IsNullOrWhiteSpace(editor.minimum)
		&& string.IsNullOrWhiteSpace(editor.maximum)
		&& editor.hotkeyMinus.isNone
		&& editor.hotkeyPlus.isNone;

	static bool isToggleBlank(ToggleBindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& editor.hotkey.isNone;

	static bool tryValidateHotkeysGlobally(IReadOnlyList<BindingFader> faders, IReadOnlyList<BindingToggle> toggles, out string? error) {
		error = null;
		var claimed = new Dictionary<HotkeyGesture, string>();
		for (int i = 0; i < faders.Count; i++) {
			BindingFader f = faders[i];
			if (!f.hotkeyMinus.isNone) {
				HotkeyGesture key = HotkeyUtil.normalize(f.hotkeyMinus);
				if (claimed.TryGetValue(key, out string? previous)) {
					error = $"Fader binding {i + 1} hotkey − ({HotkeyUtil.format(key)}) conflicts with {previous}.";
					return false;
				}
				claimed[key] = $"fader binding {i + 1} (−)";
			}
			if (!f.hotkeyPlus.isNone) {
				HotkeyGesture key = HotkeyUtil.normalize(f.hotkeyPlus);
				if (claimed.TryGetValue(key, out string? previous)) {
					error = $"Fader binding {i + 1} hotkey + ({HotkeyUtil.format(key)}) conflicts with {previous}.";
					return false;
				}
				claimed[key] = $"fader binding {i + 1} (+)";
			}
		}
		for (int i = 0; i < toggles.Count; i++) {
			BindingToggle toggle = toggles[i];
			HotkeyGesture key = HotkeyUtil.normalize(toggle.hotkey);
			if (claimed.TryGetValue(key, out string? previous)) {
				error = $"Toggle binding {i + 1} hotkey ({HotkeyUtil.format(key)}) conflicts with {previous}.";
				return false;
			}
			claimed[key] = $"toggle binding {i + 1}";
		}
		return true;
	}
}
