using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, WindowsAutostart.getCurrentUiFeedback());
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
		OsdHeightTextBox.Text = cfg.osd.heightDip.ToString(CultureInfo.InvariantCulture);
		OsdDurationTextBox.Text = cfg.osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture);
		OsdPositionComboBox.SelectedValue = cfg.osd.screenAnchor;
		if (OsdPositionComboBox.SelectedValue == null)
			OsdPositionComboBox.SelectedValue = OSDController.Config.OsdScreenAnchor.BOTTOM_RIGHT;
		KeyboardHook.Config hk = cfg.keyboardHook;
		HotkeyLongPressMsTextBox.Text = hk.longPressDurationMs.ToString(CultureInfo.InvariantCulture);
		HotkeyOptimizeNonLongPressCheckBox.IsChecked = hk.optimizeNonLongPressKeyDown;
		HotkeySuppressLongPressOnlyCheckBox.IsChecked = hk.suppressKeyForLongPressOnlyGestures;
		ConfigPathTextBox.Text = _configStore.configPath;
		UiTextFeedbackPresenter.apply(ConfigFeedbackTextBlock, _configStore.lastDiskUiFeedback);
		UiTextFeedbackPresenter.apply(InfoResultTextBox, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		UiTextFeedbackPresenter.apply(NetworkFeedbackTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		Bindings.Clear();
		foreach (BindingAbstract binding in cfg.trayApp?.bindings ?? [])
			Bindings.Add(BindingEditor.fromBinding(binding));
	}

	async void buttonApplySaveAndTest_Click(object sender, RoutedEventArgs e) {
		UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.WARNING));
		OSDController.Config.OsdScreenAnchor osdAnchor = OsdPositionComboBox.SelectedValue is OSDController.Config.OsdScreenAnchor a
			? a
			: OSDController.Config.OsdScreenAnchor.BOTTOM_RIGHT;
		(bool okBuild, AppConfig? newConfig, UiTextFeedback? buildErr) = SettingsFormDraft.tryBuild(
			IpTextBox.Text,
			PortTextBox.Text,
			TimeoutTextBox.Text,
			CacheTtlTextBox.Text,
			osdAnchor,
			OsdHeightTextBox.Text,
			OsdDurationTextBox.Text,
			HotkeyLongPressMsTextBox.Text,
			HotkeyOptimizeNonLongPressCheckBox.IsChecked == true,
			HotkeySuppressLongPressOnlyCheckBox.IsChecked == true,
			Bindings);
		if (!okBuild) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, buildErr!.Value);
			return;
		}

		_appCoordinator.beginConfigValidation();
		try {
			_appCoordinator.commitConfigFromSettingsForm(newConfig!);
			UiTextFeedbackPresenter.apply(ConfigFeedbackTextBlock, _configStore.lastDiskUiFeedback);

			var progress = new Progress<UiTextFeedback>(sample => UiTextFeedbackPresenter.apply(NetworkFeedbackTextBlock, sample));

			int timeoutMs = Math.Max(1, (int)newConfig!.mixer.timeoutMs);
			Task<UiTextFeedback> pingTask = NetworkPingTest.PingFeedbackAsync(newConfig.oscTransport.endPoint.Address, timeoutMs: timeoutMs, probeProgress: progress);
			Task<(bool Ok, string Detail)> infoTask = _mixer.QueryInfoAsync();
			await Task.WhenAll(pingTask, infoTask);

			UiTextFeedback pingResult = pingTask.Result;
			(bool infoOk, string detail) = infoTask.Result;
			UiTextFeedbackPresenter.apply(NetworkFeedbackTextBlock, pingResult);
			UiTextFeedbackPresenter.apply(InfoResultTextBox, MixerController.infoQueryDetailFeedback(infoOk, detail));
			UiTextFeedbackPresenter.apply(StatusTextBlock, MixerController.settingsApplyMixerSummaryFeedback(infoOk));
		} catch (Exception ex) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, MixerController.exceptionMessageFeedback(ex));
		} finally {
			_appCoordinator.finishConfigValidation();
		}
	}

	void buttonReload_Click(object sender, RoutedEventArgs e) {
		_configStore.loadFromDisk();
		_appCoordinator.applyConfigFromStore();
		loadFromConfigStore();
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, WindowsAutostart.getCurrentUiFeedback());
		UiTextFeedbackPresenter.apply(StatusTextBlock, ConfigStore.reloadSettingsSuccessFeedback());
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
			UiTextFeedbackPresenter.apply(StatusTextBlock, ConfigStore.explorerLaunchFailedFeedback(ex));
		}
	}

	void buttonRegisterAutostart_Click(object sender, RoutedEventArgs e) {
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, WindowsAutostart.tryRegister());
	}

	void buttonDeregisterAutostart_Click(object sender, RoutedEventArgs e) {
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, WindowsAutostart.tryDeregister());
	}

	void buttonDeregisterAutostartSplitMenu_Click(object sender, RoutedEventArgs e) {
		if (sender is not System.Windows.Controls.Button b || b.ContextMenu == null)
			return;
		b.ContextMenu.PlacementTarget = b;
		b.ContextMenu.Placement = PlacementMode.Bottom;
		b.ContextMenu.IsOpen = true;
		e.Handled = true;
	}

	void menuItemDeregisterAllAutostart_Click(object sender, RoutedEventArgs e) {
		UiTextFeedbackPresenter.apply(
			AutostartFeedbackTextBlock,
			WindowsAutostart.uiFeedbackForDeregisterAll(WindowsAutostart.tryDeregisterAllCopiesFromRun()));
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
			UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
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
		ms = KeyboardHook.Config.DEFAULT_LONG_PRESS_MS;
		if (!uint.TryParse(HotkeyLongPressMsTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
			return false;
		ms = KeyboardHook.Config.Clamped(new KeyboardHook.Config { longPressDurationMs = parsed }).longPressDurationMs;
		return true;
	}

	void finalizeHotkeyCapture(HotkeyActionEditor item, FrameworkElement focusMoveAnchor) {
		if (!_hotkeyCaptureDownUtc.HasValue)
			return;
		HotkeyGesture g = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		if (g.isNone)
			return;
		if (!HotkeyUtil.tryValidate(g, out UiTextFeedback hkFb)) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, hkFb);
			clearHotkeyCaptureTracking();
			return;
		}
		item.hotkey = g;
		uint thresholdMs = tryParseHotkeyLongPressMsForCapture(out uint lp) ? lp : KeyboardHook.Config.DEFAULT_LONG_PRESS_MS;
		double heldMs = (DateTime.UtcNow - _hotkeyCaptureDownUtc.Value).TotalMilliseconds;
		item.longPress = heldMs >= thresholdMs;
		UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
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

		if (!HotkeyUtil.tryValidate(hotkey, out UiTextFeedback hkFb)) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, hkFb);
			e.Handled = true;
			return;
		}

		_hotkeyCaptureGesture = HotkeyUtil.normalize(hotkey);
		_hotkeyCaptureDownUtc = DateTime.UtcNow;
		_hotkeyCaptureAwaitingRelease = true;
		_hotkeyCaptureItem = item;
		UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		e.Handled = true;
	}

	static void moveFocusAwayAfterAssign(object? captureElement) {
		if (captureElement is not FrameworkElement fe)
			return;
		_ = fe.MoveFocus(new TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
	}

}

