using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
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

	public static readonly DependencyProperty isDragInProgressProperty =
		DependencyProperty.Register(nameof(isDragInProgress), typeof(bool), typeof(ConfigWindow), new PropertyMetadata(false));

	public static readonly DependencyProperty dragItemProperty =
		DependencyProperty.Register(nameof(dragItem), typeof(object), typeof(ConfigWindow), new PropertyMetadata(null));

	public static readonly DependencyProperty dragOwnerListProperty =
		DependencyProperty.Register(nameof(dragOwnerList), typeof(ItemsControl), typeof(ConfigWindow), new PropertyMetadata(null));

	public static readonly DependencyProperty dragPlaceholderHeightProperty =
		DependencyProperty.Register(nameof(dragPlaceholderHeight), typeof(double), typeof(ConfigWindow), new PropertyMetadata(0d));

	public bool isDragInProgress {
		get => (bool)GetValue(isDragInProgressProperty);
		set => SetValue(isDragInProgressProperty, value);
	}

	public object? dragItem {
		get => (object?)GetValue(dragItemProperty);
		set => SetValue(dragItemProperty, value);
	}

	public ItemsControl? dragOwnerList {
		get => (ItemsControl?)GetValue(dragOwnerListProperty);
		set => SetValue(dragOwnerListProperty, value);
	}

	public double dragPlaceholderHeight {
		get => (double)GetValue(dragPlaceholderHeightProperty);
		set => SetValue(dragPlaceholderHeightProperty, value);
	}

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

	readonly Dictionary<ItemsControl, InsertionLineAdorner> _insertionLineAdorners = [];

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
		applyAutostartFeedback(WindowsAutostart.getCurrentUiFeedback());
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

	static T? findVisualAncestor<T>(DependencyObject start) where T : DependencyObject {
		DependencyObject? cur = start;
		while (cur != null) {
			if (cur is T match)
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
		// Fluent template sets ContentPresenter.Margin = Padding on all sides; our body already ends with spacing
		// (e.g. last hotkey row Margin bottom), so that stacks with the template and reads as double bottom inset when expanded.
		if (exp.Template?.FindName("ContentPresenter", exp) is ContentPresenter contentPresenter) {
			Thickness p = exp.Padding;
			contentPresenter.Margin = new Thickness(p.Left, p.Top, p.Right, 0d);
		}
	}

	void applyBindingCardChevronDim(Expander exp) {
		if (!tryFindFluentExpanderChevronGrid(exp, out UIElement? grid))
			return;
		if (grid == null)
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
		HotkeyAcceptMacroChordKeyOrderCheckBox.IsChecked = hk.acceptMacroChordKeyOrder;
		ConfigPathTextBox.Text = _configStore.configPathForUi;
		UiTextFeedbackPresenter.apply(ConfigFeedbackTextBlock, _configStore.lastDiskUiFeedback);
		UiTextFeedbackPresenter.apply(InfoResultTextBox, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		UiTextFeedbackPresenter.apply(StatusTextBlock, new UiTextFeedback("", UiTextFeedbackKind.DEFAULT));
		Bindings.Clear();
		foreach (BindingAbstract binding in cfg.trayApp?.bindings ?? [])
			Bindings.Add(BindingEditor.fromBinding(binding));
	}

	static string formatLatencyCellText(int? rttMs) {
		if (rttMs == null)
			return "—";
		int ms = rttMs.Value;
		if (ms == 0)
			return "<1";
		return ms.ToString(CultureInfo.InvariantCulture);
	}

	enum ConfigUiStatus {
		MUTED,
		SUCCESS,
		CAUTION,
		CRITICAL,
	}

	Brush tryGetThemeBrush(string key, Brush fallback) =>
		TryFindResource(key) as Brush ?? fallback;

	Brush brushForStatus(ConfigUiStatus status) => status switch {
		ConfigUiStatus.MUTED => tryGetThemeBrush("TextFillColorSecondaryBrush", Brushes.Gray),
		// Prefer system/theme success; fall back to the more-legible green.
		ConfigUiStatus.SUCCESS => tryGetThemeBrush("SystemFillColorSuccessBrush", Brushes.LimeGreen),
		ConfigUiStatus.CAUTION => tryGetThemeBrush("SystemFillColorCautionBrush", Brushes.DarkOrange),
		ConfigUiStatus.CRITICAL => tryGetThemeBrush("SystemFillColorCriticalBrush", Brushes.IndianRed),
		_ => tryGetThemeBrush("TextFillColorPrimaryBrush", Brushes.White),
	};

	ConfigUiStatus responseStatus(int completed, int received) {
		if (completed <= 0)
			return ConfigUiStatus.MUTED;
		if (received <= 0)
			return ConfigUiStatus.CRITICAL;
		return received < completed ? ConfigUiStatus.CAUTION : ConfigUiStatus.SUCCESS;
	}

	ConfigUiStatus latencyStatus(int timeoutMs, int? rttMs) {
		if (rttMs == null)
			return ConfigUiStatus.MUTED;
		if (timeoutMs <= 0)
			return ConfigUiStatus.MUTED;
		double ratio = rttMs.Value / (double)timeoutMs;
		if (ratio < 0.10)
			return ConfigUiStatus.SUCCESS;
		if (ratio < 0.50)
			return ConfigUiStatus.CAUTION;
		return ConfigUiStatus.CRITICAL;
	}

	void applyLatencyStatsToUi(int timeoutMs, RttStatsSnapshot ping, RttStatsSnapshot osc) {
		PingMinTextBlock.Text = formatLatencyCellText(ping.minMs);
		PingMedianTextBlock.Text = formatLatencyCellText(ping.medianMs);
		PingMaxTextBlock.Text = formatLatencyCellText(ping.maxMs);
		PingLossTextBlock.Text = ping.completedCount == 0 ? "—" : ping.receivedCount.ToString(CultureInfo.InvariantCulture);

		OscMinTextBlock.Text = formatLatencyCellText(osc.minMs);
		OscMedianTextBlock.Text = formatLatencyCellText(osc.medianMs);
		OscMaxTextBlock.Text = formatLatencyCellText(osc.maxMs);
		OscLossTextBlock.Text = osc.completedCount == 0 ? "—" : osc.receivedCount.ToString(CultureInfo.InvariantCulture);

		int completed = ping.completedCount;
		LossUnitTextBlock.Text = "/" + completed.ToString(CultureInfo.InvariantCulture);

		PingLossTextBlock.Foreground = brushForStatus(responseStatus(ping.completedCount, ping.receivedCount));
		OscLossTextBlock.Foreground = brushForStatus(responseStatus(osc.completedCount, osc.receivedCount));

		PingMinTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, ping.minMs));
		PingMedianTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, ping.medianMs));
		PingMaxTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, ping.maxMs));

		OscMinTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, osc.minMs));
		OscMedianTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, osc.medianMs));
		OscMaxTextBlock.Foreground = brushForStatus(latencyStatus(timeoutMs, osc.maxMs));
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
			HotkeyAcceptMacroChordKeyOrderCheckBox.IsChecked == true,
			Bindings);
		if (!okBuild) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, buildErr!.Value);
			return;
		}

		_appCoordinator.beginConfigValidation();
		try {
			_appCoordinator.commitConfigFromSettingsForm(newConfig!);
			UiTextFeedbackPresenter.apply(ConfigFeedbackTextBlock, _configStore.lastDiskUiFeedback);

			int timeoutMs = Math.Max(1, (int)newConfig!.mixer.timeoutMs);
			const int probes = 10;

			var pingStats = new RttStatsAccumulator();
			var oscStats = new RttStatsAccumulator();

			applyLatencyStatsToUi(timeoutMs, pingStats.snapshot(), oscStats.snapshot());

			BindingFader? firstFader = (newConfig.trayApp?.bindings ?? []).OfType<BindingFader>().FirstOrDefault();
			bool oscUsesBinding = firstFader != null;
			string oscAddress = oscUsesBinding ? firstFader!.address : "/info";
			OscHeaderTextBlock.Text = oscUsesBinding ? "OSC Binding #1" : "OSC /info";

			bool lastInfoOk = false;
			string lastInfoDetail = "";

			async Task<int?> probeOscOnceAsync() {
				if (oscUsesBinding) {
					float? reply = await _mixer.QueryFaderAsync(oscAddress);
					if (reply == null)
						return null;
				} else {
					(bool ok, string detail) = await _mixer.QueryInfoAsync();
					lastInfoOk = ok;
					lastInfoDetail = detail;
					if (!ok)
						return null;
				}

				if (!_mixer.tryGetMeasuredLatency(oscAddress, out TimeSpan latency))
					return null;
				double ms = latency.TotalMilliseconds;
				if (!double.IsFinite(ms) || ms < 0 || ms > int.MaxValue)
					return null;
				return (int)Math.Round(ms);
			}

			for (int i = 0; i < probes; i++) {
				Task<int?> pingTask = NetworkPingTest.PingOnceAsync(newConfig.oscTransport.endPoint.Address, timeoutMs);
				Task<int?> oscTask = probeOscOnceAsync();
				await Task.WhenAll(pingTask, oscTask);

				pingStats.push(pingTask.Result);
				oscStats.push(oscTask.Result);

				RttStatsSnapshot pingSnap = pingStats.snapshot();
				RttStatsSnapshot oscSnap = oscStats.snapshot();
				applyLatencyStatsToUi(timeoutMs, pingSnap, oscSnap);
			}

			if (oscUsesBinding) {
				(lastInfoOk, lastInfoDetail) = await _mixer.QueryInfoAsync();
			}

			UiTextFeedbackPresenter.apply(InfoResultTextBox, MixerController.infoQueryDetailFeedback(lastInfoOk, lastInfoDetail));
			UiTextFeedbackPresenter.apply(StatusTextBlock, MixerController.settingsApplyMixerSummaryFeedback(lastInfoOk));
		} catch (Exception ex) {
			UiTextFeedbackPresenter.apply(StatusTextBlock, MixerController.exceptionMessageFeedback(ex));
		} finally {
			_appCoordinator.finishConfigValidation();
		}
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
		applyAutostartFeedback(WindowsAutostart.tryRegister());
	}

	void buttonDeregisterAutostart_Click(object sender, RoutedEventArgs e) {
		applyAutostartFeedback(WindowsAutostart.tryDeregister());
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
		applyAutostartFeedback(WindowsAutostart.uiFeedbackForDeregisterAll(WindowsAutostart.tryDeregisterAllCopiesFromRun()));
	}

	void applyAutostartFeedback(WindowsAutostart.UiFeedbackDetail detail) {
		UiTextFeedbackPresenter.apply(AutostartFeedbackTextBlock, detail.feedback);
		if (string.IsNullOrEmpty(detail.pathOrNull)) {
			AutostartFeedbackPathTextBox.Text = "";
			AutostartFeedbackPathTextBox.Visibility = Visibility.Collapsed;
			return;
		}
		AutostartFeedbackPathTextBox.Text = detail.pathOrNull;
		AutostartFeedbackPathTextBox.Visibility = Visibility.Visible;
	}

	void applyAutostartFeedback(UiTextFeedback feedback) =>
		applyAutostartFeedback(new WindowsAutostart.UiFeedbackDetail(feedback, null));

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
		HotkeyGesture normUp = HotkeyUtil.normalize(up);
		HotkeyGesture normDown = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		bool acceptMacroKeyUpOrder = HotkeyAcceptMacroChordKeyOrderCheckBox.IsChecked == true;
		if (acceptMacroKeyUpOrder) {
			if (normUp.keyCode != normDown.keyCode)
				return;
		} else {
			if (normUp != normDown)
				return;
		}
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
		HotkeyGesture normUp = HotkeyUtil.normalize(up);
		HotkeyGesture normDown = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		bool acceptMacroKeyUpOrder = HotkeyAcceptMacroChordKeyOrderCheckBox.IsChecked == true;
		if (acceptMacroKeyUpOrder) {
			if (normUp.keyCode != normDown.keyCode)
				return;
		} else {
			if (normUp != normDown)
				return;
		}
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

public sealed class DragActivePlaceholderMultiConverter : IMultiValueConverter {
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
		if (values.Length < 5)
			return false;
		object? item = values[0];
		object? draggedItem = values[1];
		bool isDragging = values[2] is bool b && b;
		object? list = values[3];
		object? activeList = values[4];
		if (!isDragging)
			return false;
		return ReferenceEquals(item, draggedItem) && ReferenceEquals(list, activeList);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}

partial class ConfigWindow {
	const string REORDER_DRAG_FORMAT = "WindowsOscVolumeControl.ReorderItem";

	void reorderThumb_DragStarted(object sender, DragStartedEventArgs e) {
		if (sender is not Thumb thumb)
			return;
		if (thumb.DataContext == null)
			return;

		ItemsControl? list = findVisualAncestor<ItemsControl>(thumb);
		if (list == null)
			return;

		object item = thumb.DataContext;
		if (item is BindingEditor be && be.isDeleted)
			return;
		if (item is HotkeyActionEditor he && he.isDeleted)
			return;

		FrameworkElement? container = list switch {
			System.Windows.Controls.ListBox lb => lb.ContainerFromElement(thumb) as FrameworkElement,
			_ => null,
		};

		dragOwnerList = list;
		dragItem = item;
		isDragInProgress = true;
		dragPlaceholderHeight = container?.ActualHeight ?? 0d;

		try {
			hideInsertionLine(list);
			var data = new System.Windows.DataObject();
			data.SetData(REORDER_DRAG_FORMAT, item);
			_ = System.Windows.DragDrop.DoDragDrop(thumb, data, System.Windows.DragDropEffects.Move);
		} finally {
			hideInsertionLine(list);
			isDragInProgress = false;
			dragItem = null;
			dragOwnerList = null;
			dragPlaceholderHeight = 0d;
		}
	}

	void reorderList_DragLeave(object sender, System.Windows.DragEventArgs e) {
		// DragLeave bubbles for child-to-child transitions inside the list; only hide when leaving the list itself.
		if (sender is not ItemsControl list)
			return;
		if (!ReferenceEquals(e.OriginalSource, list))
			return;
		hideInsertionLine(list);
	}

	void reorderList_DragOver(object sender, System.Windows.DragEventArgs e) {
		if (sender is not ItemsControl list) {
			e.Effects = System.Windows.DragDropEffects.None;
			e.Handled = true;
			return;
		}

		if (!isDragInProgress || dragOwnerList == null || !ReferenceEquals(list, dragOwnerList)) {
			hideInsertionLine(list);
			e.Effects = System.Windows.DragDropEffects.None;
			e.Handled = true;
			return;
		}

		if (!e.Data.GetDataPresent(REORDER_DRAG_FORMAT) || dragItem == null) {
			hideInsertionLine(list);
			e.Effects = System.Windows.DragDropEffects.None;
			e.Handled = true;
			return;
		}

		System.Windows.Point p = e.GetPosition(list);
		int dropIndex = computeDropIndex(list, p, dragItem, out double lineY);
		showInsertionLine(list, lineY);

		e.Effects = System.Windows.DragDropEffects.Move;
		e.Handled = true;
	}

	void reorderList_Drop(object sender, System.Windows.DragEventArgs e) {
		if (sender is not ItemsControl list) {
			e.Effects = System.Windows.DragDropEffects.None;
			e.Handled = true;
			return;
		}

		try {
			if (!isDragInProgress || dragOwnerList == null || !ReferenceEquals(list, dragOwnerList) || dragItem == null) {
				e.Effects = System.Windows.DragDropEffects.None;
				return;
			}
			if (!e.Data.GetDataPresent(REORDER_DRAG_FORMAT)) {
				e.Effects = System.Windows.DragDropEffects.None;
				return;
			}

			System.Windows.Point p = e.GetPosition(list);
			int dropIndex = computeDropIndex(list, p, dragItem, out _);
			tryMoveDraggedItem(list, dragItem, dropIndex);
			e.Effects = System.Windows.DragDropEffects.Move;
		} finally {
			hideInsertionLine(list);
			e.Handled = true;
		}
	}

	void tryMoveDraggedItem(ItemsControl list, object dragged, int dropIndex) {
		if (dragged is BindingEditor bed) {
			int oldIndex = Bindings.IndexOf(bed);
			if (oldIndex < 0)
				return;
			int newIndex = dropIndexToMoveIndex(oldIndex, dropIndex, Bindings.Count);
			if (oldIndex == newIndex)
				return;
			Bindings.Move(oldIndex, newIndex);
			return;
		}

		if (dragged is HotkeyActionEditor hed && list.DataContext is BindingEditor owner) {
			ObservableCollection<HotkeyActionEditor> hotkeys = owner.hotkeys;
			int oldIndex = hotkeys.IndexOf(hed);
			if (oldIndex < 0)
				return;
			int newIndex = dropIndexToMoveIndex(oldIndex, dropIndex, hotkeys.Count);
			if (oldIndex == newIndex)
				return;
			hotkeys.Move(oldIndex, newIndex);
		}
	}

	static int dropIndexToMoveIndex(int oldIndex, int dropIndex, int count) {
		int idx = Math.Clamp(dropIndex, 0, count);
		// Drop indices are between items (0..count). Convert to a valid Move() index (0..count-1).
		// When moving downward past the old position, removing the item shifts the target left by 1.
		int moveIdx = idx > oldIndex ? idx - 1 : idx;
		return Math.Clamp(moveIdx, 0, Math.Max(0, count - 1));
	}

	int computeDropIndex(ItemsControl list, System.Windows.Point p, object dragged, out double insertionLineY) {
		insertionLineY = 0d;
		int n = list.Items.Count;
		if (n <= 0)
			return 0;

		int draggedIndex = list.Items.IndexOf(dragged);
		FrameworkElement? draggedContainer = draggedIndex >= 0
			? list.ItemContainerGenerator.ContainerFromIndex(draggedIndex) as FrameworkElement
			: null;
		System.Windows.Point draggedTopLeft = draggedContainer != null
			? draggedContainer.TranslatePoint(new System.Windows.Point(0, 0), list)
			: new System.Windows.Point(0, 0);

		// If hovering over the dragged item's own row, use the row's top/bottom boundary depending on pointer half.
		DependencyObject? hit = list.InputHitTest(p) as DependencyObject;
		if (hit != null) {
			if (findVisualAncestor<ListBoxItem>(hit) is ListBoxItem li && ReferenceEquals(li.DataContext, dragged)) {
				System.Windows.Point topLeft = li.TranslatePoint(new System.Windows.Point(0, 0), list);
				double midY = topLeft.Y + (li.ActualHeight / 2d);
				if (p.Y < midY) {
					insertionLineY = topLeft.Y;
					return draggedIndex < 0 ? 0 : draggedIndex;
				}
				insertionLineY = topLeft.Y + li.ActualHeight;
				return draggedIndex < 0 ? 0 : Math.Min(draggedIndex + 1, n);
			}
		}

		// Scan actual containers in original list order (including the dragged item's placeholder container).
		FrameworkElement? lastContainer = null;
		double lastBottom = 0d;
		for (int i = 0; i < n; i++) {
			if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c)
				continue;
			System.Windows.Point topLeft = c.TranslatePoint(new System.Windows.Point(0, 0), list);
			double midY = topLeft.Y + (c.ActualHeight / 2d);
			if (p.Y < midY) {
				insertionLineY = topLeft.Y;
				return i;
			}
			lastContainer = c;
			lastBottom = topLeft.Y + c.ActualHeight;
		}

		insertionLineY = lastContainer != null ? lastBottom : 0d;
		return n;
	}

	void showInsertionLine(ItemsControl list, double y) {
		AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
		if (layer == null)
			return;
		if (!_insertionLineAdorners.TryGetValue(list, out InsertionLineAdorner? adorner)) {
			adorner = new InsertionLineAdorner(list);
			_insertionLineAdorners[list] = adorner;
			layer.Add(adorner);
		}
		if (Math.Abs(adorner.y - y) < 0.5)
			return;
		adorner.y = y;
		adorner.InvalidateVisual();
	}

	void hideInsertionLine(ItemsControl list) {
		if (!_insertionLineAdorners.TryGetValue(list, out InsertionLineAdorner? adorner))
			return;
		AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
		if (layer == null)
			return;
		layer.Remove(adorner);
		_insertionLineAdorners.Remove(list);
	}

	sealed class InsertionLineAdorner : Adorner {
		public double y;

		public InsertionLineAdorner(UIElement adornedElement) : base(adornedElement) {
			IsHitTestVisible = false;
		}

		protected override void OnRender(DrawingContext drawingContext) {
			if (y < 0)
				return;
			double width = AdornedElement.RenderSize.Width;
			if (width <= 0)
				return;

			Brush b = (AdornedElement as FrameworkElement)?.TryFindResource("TextFillColorPrimaryBrush") as Brush
			          ?? Brushes.White;

			var pen = new System.Windows.Media.Pen(b, 2.0);
			pen.Freeze();

			double yy = Math.Round(y) + 0.5;
			drawingContext.DrawLine(pen, new System.Windows.Point(0, yy), new System.Windows.Point(width, yy));
		}
	}
}

