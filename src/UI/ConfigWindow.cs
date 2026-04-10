using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using TraversalRequest = System.Windows.Input.TraversalRequest;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WindowsOscVolumeControl;

public partial class ConfigWindow : Window {
	public const string HotkeyAssignmentCaptureTag = "HotkeyAssignmentCapture";
	public const string BindingCardRestoreTag = "BindingCardRestore";

	HotkeyActionEditor? _hotkeyCaptureItem;
	DateTime? _hotkeyCaptureDownUtc;
	HotkeyGesture _hotkeyCaptureGesture;
	bool _hotkeyCaptureAwaitingRelease;

	readonly MixerController _mixer;
	readonly TrayController _trayController;
	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;

	/// <summary>Fluent Expander template: <see cref="Expander"/> → HeaderSite → ChevronGrid.</summary>
	readonly Dictionary<Expander, (BindingEditor bed, PropertyChangedEventHandler handler)> _bindingCardChevronDimHooks = [];

	public ObservableCollection<BindingEditor> Bindings { get; } = [];

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

	static T? findDataContextAncestor<T>(DependencyObject start) where T : class {
		DependencyObject? cur = start;
		while (cur != null) {
			if (cur is FrameworkElement { DataContext: T match })
				return match;
			cur = VisualTreeHelper.GetParent(cur);
		}
		return null;
	}

	static bool isUnderBindingCardRestore(DependencyObject? hit) {
		while (hit != null) {
			if (hit is System.Windows.Controls.Button { Tag: string s } && s == BindingCardRestoreTag)
				return true;
			hit = VisualTreeHelper.GetParent(hit);
		}
		return false;
	}

	const double BINDING_CARD_SOFT_DELETE_CHEVRON_OPACITY = 0.38;

	static bool tryFindFluentExpanderChevronGrid(Expander exp, out UIElement? chevronGrid) {
		chevronGrid = null;
		exp.ApplyTemplate();
		if (exp.Template?.FindName("HeaderSite", exp) is not ToggleButton headerSite)
			return false;
		headerSite.ApplyTemplate();
		if (headerSite.Template?.FindName("ChevronGrid", headerSite) is not UIElement grid)
			return false;
		chevronGrid = grid;
		return true;
	}

	/// <summary>Fluent expander body uses <c>ExpanderContentBackground</c>; remap to <c>ExpanderHeaderBackground</c> for binding cards only.</summary>
	static void applyBindingCardExpanderContentFill(Expander exp) {
		if (exp.TryFindResource("ExpanderHeaderBackground") is Brush headerBg)
			exp.Resources["ExpanderContentBackground"] = headerBg;
		exp.ApplyTemplate();
		if (exp.Template?.FindName("ToggleButtonBorder", exp) is Border headerChrome) {
			headerChrome.BorderThickness = new Thickness(0);
			headerChrome.BorderBrush = Brushes.Transparent;
		}
		if (exp.Template?.FindName("ContentPresenterBorder", exp) is Border contentChrome) {
			contentChrome.SetResourceReference(Border.BackgroundProperty, "ExpanderHeaderBackground");
			contentChrome.BorderThickness = new Thickness(0);
			contentChrome.BorderBrush = Brushes.Transparent;
		}
	}

	void applyBindingCardChevronDim(Expander exp) {
		if (!tryFindFluentExpanderChevronGrid(exp, out UIElement? grid))
			return;
		bool dim = exp.DataContext is BindingEditor { isDeleted: true };
		grid.Opacity = dim ? BINDING_CARD_SOFT_DELETE_CHEVRON_OPACITY : 1d;
	}

	void hookBindingCardChevronDim(Expander exp) {
		unhookBindingCardChevronDim(exp);
		if (exp.DataContext is not BindingEditor bed)
			return;
		PropertyChangedEventHandler h = (_, args) => {
			if (args.PropertyName is not null && args.PropertyName != nameof(BindingEditor.isDeleted))
				return;
			exp.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => applyBindingCardChevronDim(exp)));
		};
		bed.PropertyChanged += h;
		_bindingCardChevronDimHooks[exp] = (bed, h);
		applyBindingCardChevronDim(exp);
	}

	void unhookBindingCardChevronDim(Expander exp) {
		if (!_bindingCardChevronDimHooks.Remove(exp, out (BindingEditor bed, PropertyChangedEventHandler handler) pair))
			return;
		pair.bed.PropertyChanged -= pair.handler;
	}

	void bindingCard_Expander_Loaded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		applyBindingCardExpanderContentFill(exp);
		exp.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => hookBindingCardChevronDim(exp)));
	}

	void bindingCard_Expander_Unloaded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		unhookBindingCardChevronDim(exp);
	}

	void bindingCard_Expander_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
		if (sender is not Expander exp)
			return;
		unhookBindingCardChevronDim(exp);
		applyBindingCardExpanderContentFill(exp);
		exp.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => hookBindingCardChevronDim(exp)));
	}

	void loadFromConfigStore() {
		AppConfig cfg = _configStore.appConfig;
		IpTextBox.Text = cfg.oscTransport.endPoint.Address.ToString();
		PortTextBox.Text = cfg.oscTransport.endPoint.Port.ToString(CultureInfo.InvariantCulture);
		TimeoutTextBox.Text = cfg.mixer.timeoutMs.ToString(CultureInfo.InvariantCulture);
		CacheTtlTextBox.Text = cfg.mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture);
		OsdHeightTextBox.Text = cfg.osd.HeightPx.ToString(CultureInfo.InvariantCulture);
		OsdDurationTextBox.Text = cfg.osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture);
		BindingManager.Config tray = cfg.trayApp ?? new BindingManager.Config();
		HotkeyLongPressMsTextBox.Text = tray.longPressDurationMs.ToString(CultureInfo.InvariantCulture);
		HotkeyOptimizeNonLongPressCheckBox.IsChecked = tray.optimizeNonLongPressKeyDown;
		ConfigPathTextBox.Text = _configStore.configPath;
		ConfigFeedbackTextBlock.Text = _configStore.lastDiskFeedback;
		DiskFeedbackTextBlock.Text = _configStore.lastDiskFeedback;
		InfoResultTextBox.Text = "";
		NetworkFeedbackTextBlock.Text = "";
		StatusTextBlock.Text = "";
		Bindings.Clear();
		foreach (BindingAbstract binding in cfg.trayApp?.bindings ?? [])
			Bindings.Add(BindingEditor.fromBinding(binding));
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

	void buttonAddBinding_Click(object sender, RoutedEventArgs e) {
		var ed = new BindingEditor {
			type = BindingEditorType.FADER,
			name = "",
			address = "",
			minimum = "0",
			maximum = "1",
		};
		ed.hotkeys.Add(ed.createHotkeyEditor());
		Bindings.Add(ed);
	}

	void buttonBindingDelete_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: BindingEditor item })
			item.isDeleted = true;
	}

	void buttonBindingRestore_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: BindingEditor item })
			item.isDeleted = false;
	}

	void bindingCard_Expander_Expanded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		if (exp.DataContext is BindingEditor bed && bed.isDeleted)
			exp.IsExpanded = false;
		applyBindingCardExpanderContentFill(exp);
	}

	void bindingCard_Expander_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left)
			return;
		if (sender is not Expander exp || exp.DataContext is not BindingEditor bed || !bed.isDeleted)
			return;
		if (isUnderBindingCardRestore(e.OriginalSource as DependencyObject))
			return;
		e.Handled = true;
	}

	void bindingCard_Expander_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is not Expander exp || exp.DataContext is not BindingEditor bed || !bed.isDeleted)
			return;
		if (e.Key != Key.Space && e.Key != Key.Enter)
			return;
		if (isUnderBindingCardRestore(Keyboard.FocusedElement as DependencyObject))
			return;
		e.Handled = true;
	}

	void buttonAddHotkeyToBinding_Click(object sender, RoutedEventArgs e) {
		if (sender is not DependencyObject d)
			return;
		BindingEditor? bed = findDataContextAncestor<BindingEditor>(d);
		if (bed == null)
			return;
		bed.hotkeys.Add(bed.createHotkeyEditor());
	}

	void buttonHotkeyDelete_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item })
			item.isDeleted = true;
	}

	void buttonHotkeyRestore_Click(object sender, RoutedEventArgs e) {
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item })
			item.isDeleted = false;
	}

	void hotkeyControl_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
		_appCoordinator.setConfiguredHotkeysEnabled(false);
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item }) {
			item.isHotkeyCaptureActive = true;
			item.hotkey = HotkeyGesture.None;
			StatusTextBlock.Text = "";
			clearHotkeyCaptureTracking();
			_hotkeyCaptureItem = item;
		}
	}

	void hotkeyControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item }) {
			item.isHotkeyCaptureActive = false;
			if (ReferenceEquals(_hotkeyCaptureItem, item))
				clearHotkeyCaptureTracking();
		}
		_appCoordinator.setConfiguredHotkeysEnabled(true);
	}

	void clearHotkeyCaptureTracking() {
		_hotkeyCaptureItem = null;
		_hotkeyCaptureDownUtc = null;
		_hotkeyCaptureGesture = HotkeyGesture.None;
		_hotkeyCaptureAwaitingRelease = false;
	}

	bool tryParseHotkeyLongPressMsForCapture(out uint ms) {
		ms = BindingManager.Config.DEFAULT_LONG_PRESS_MS;
		if (!uint.TryParse(HotkeyLongPressMsTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
			return false;
		ms = BindingManager.Config.clampLongPressDurationMs(parsed);
		return true;
	}

	void finalizeHotkeyCapture(HotkeyActionEditor item, FrameworkElement focusMoveAnchor) {
		if (!_hotkeyCaptureDownUtc.HasValue)
			return;
		HotkeyGesture g = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		if (g.isNone)
			return;
		if (!HotkeyUtil.tryValidate(g, out string err)) {
			StatusTextBlock.Foreground = Brushes.IndianRed;
			StatusTextBlock.Text = err;
			clearHotkeyCaptureTracking();
			return;
		}
		item.hotkey = g;
		uint thresholdMs = tryParseHotkeyLongPressMsForCapture(out uint lp) ? lp : BindingManager.Config.DEFAULT_LONG_PRESS_MS;
		double heldMs = (DateTime.UtcNow - _hotkeyCaptureDownUtc.Value).TotalMilliseconds;
		item.longPress = heldMs >= thresholdMs;
		StatusTextBlock.Text = "";
		clearHotkeyCaptureTracking();
		focusMoveAnchor.Dispatcher.BeginInvoke(DispatcherPriority.Background, moveFocusAwayAfterAssign, focusMoveAnchor);
	}

	void configWindow_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (e.Key is not Key.Tab)
			return;
		if (Keyboard.FocusedElement is not System.Windows.Controls.Button focusedBtn)
			return;
		if (focusedBtn.Tag is not string tag || tag != HotkeyAssignmentCaptureTag)
			return;
		if (focusedBtn.DataContext is not HotkeyActionEditor item)
			return;
		if (!item.isHotkeyCaptureActive)
			return;
		beginHotkeyCaptureKeyDown(e, item);
	}

	void configWindow_PreviewKeyUp(object sender, KeyEventArgs e) {
		if (e.Key is not Key.Tab)
			return;
		if (Keyboard.FocusedElement is not System.Windows.Controls.Button focusedBtn)
			return;
		if (focusedBtn.Tag is not string tag || tag != HotkeyAssignmentCaptureTag)
			return;
		if (focusedBtn.DataContext is not HotkeyActionEditor item)
			return;
		if (!item.isHotkeyCaptureActive || !_hotkeyCaptureAwaitingRelease || !ReferenceEquals(_hotkeyCaptureItem, item))
			return;
		HotkeyGesture up = HotkeyUtil.fromKeyEventArgs(e);
		if (HotkeyUtil.normalize(up).keyCode != HotkeyUtil.normalize(_hotkeyCaptureGesture).keyCode)
			return;
		e.Handled = true;
		finalizeHotkeyCapture(item, focusedBtn);
	}

	void hotkeyRow_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement fe && fe.DataContext is HotkeyActionEditor item && item.isHotkeyCaptureActive)
			beginHotkeyCaptureKeyDown(e, item);
	}

	void hotkeyRow_PreviewKeyUp(object sender, KeyEventArgs e) {
		if (sender is not FrameworkElement fe || fe.DataContext is not HotkeyActionEditor item)
			return;
		if (!item.isHotkeyCaptureActive || !_hotkeyCaptureAwaitingRelease || !ReferenceEquals(_hotkeyCaptureItem, item))
			return;
		HotkeyGesture up = HotkeyUtil.fromKeyEventArgs(e);
		if (HotkeyUtil.normalize(up).keyCode != HotkeyUtil.normalize(_hotkeyCaptureGesture).keyCode)
			return;
		e.Handled = true;
		finalizeHotkeyCapture(item, fe);
	}

	void beginHotkeyCaptureKeyDown(KeyEventArgs e, HotkeyActionEditor item) {
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

		_hotkeyCaptureGesture = HotkeyUtil.normalize(hotkey);
		_hotkeyCaptureDownUtc = DateTime.UtcNow;
		_hotkeyCaptureAwaitingRelease = true;
		_hotkeyCaptureItem = item;
		StatusTextBlock.Text = "";
		e.Handled = true;
	}

	static void moveFocusAwayAfterAssign(object? captureElement) {
		if (captureElement is not FrameworkElement fe)
			return;
		_ = fe.MoveFocus(new TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
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
		if (!tryParseUInt(HotkeyLongPressMsTextBox.Text, BindingManager.Config.MIN_LONG_PRESS_MS, BindingManager.Config.MAX_LONG_PRESS_MS, "Long-press duration", out uint hotkeyLongPressMs, out error))
			return false;

		var built = new List<BindingAbstract>();
		for (int i = 0; i < Bindings.Count; i++) {
			BindingEditor editor = Bindings[i];
			if (editor.isDeleted || isBindingBlank(editor))
				continue;

			if (string.IsNullOrWhiteSpace(editor.name) || string.IsNullOrWhiteSpace(editor.address)) {
				error = $"Binding {i + 1} requires name and OSC address.";
				return false;
			}

			if (editor.type == BindingEditorType.FADER) {
				if (!tryParseFloat(editor.minimum, "Minimum", out float min, out error)
				    || !tryParseFloat(editor.maximum, "Maximum", out float max, out error))
					return false;
				if (min > max) {
					error = $"Binding {i + 1}: minimum must be less than or equal to maximum.";
					return false;
				}
				min = FaderFloatUtil.RoundToBindingDecimals(min);
				max = FaderFloatUtil.RoundToBindingDecimals(max);
				var fader = new BindingFader {
					name = editor.name.Trim(),
					address = editor.address.Trim(),
					minimum = min,
					maximum = max,
				};
				for (int h = 0; h < editor.hotkeys.Count; h++) {
					HotkeyActionEditor hk = editor.hotkeys[h];
					if (hk.isDeleted || isHotkeyRowBlank(hk))
						continue;
					if (!hk.tryBuildModel(editor.type, out HotkeyAction? action, out string? hkErr)) {
						error = $"Binding {i + 1}, hotkey {h + 1}: {hkErr}";
						return false;
					}
					fader.hotkeys.Add(action);
				}
				built.Add(fader);
			} else {
				var toggle = new BindingToggle {
					name = editor.name.Trim(),
					address = editor.address.Trim(),
				};
				for (int h = 0; h < editor.hotkeys.Count; h++) {
					HotkeyActionEditor hk = editor.hotkeys[h];
					if (hk.isDeleted || isHotkeyRowBlank(hk))
						continue;
					if (!hk.tryBuildModel(editor.type, out HotkeyAction? action, out string? hkErr)) {
						error = $"Binding {i + 1}, hotkey {h + 1}: {hkErr}";
						return false;
					}
					toggle.hotkeys.Add(action);
				}
				built.Add(toggle);
			}
		}

		if (built.OfType<BindingFader>().FirstOrDefault() == null) {
			error = "Add at least one non-deleted fader binding with name and address.";
			return false;
		}

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
				bindings = built,
				longPressDurationMs = BindingManager.Config.clampLongPressDurationMs(hotkeyLongPressMs),
				optimizeNonLongPressKeyDown = HotkeyOptimizeNonLongPressCheckBox.IsChecked == true,
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

	static bool isBindingBlank(BindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& string.IsNullOrWhiteSpace(editor.minimum)
		&& string.IsNullOrWhiteSpace(editor.maximum)
		&& editor.hotkeys.Count == 0;

	static bool isHotkeyRowBlank(HotkeyActionEditor hk) => hk.hotkey.isNone;

}
