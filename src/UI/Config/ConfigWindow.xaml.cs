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
using System.Windows.Forms;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Tray;
using Key = System.Windows.Input.Key;
using Keyboard = System.Windows.Input.Keyboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using TraversalRequest = System.Windows.Input.TraversalRequest;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace WindowsOscVolumeControl.UI.Config;

public partial class ConfigWindow : Window {
	public const string HotkeyAssignmentCaptureTag = "HotkeyAssignmentCapture";
	public const string BindingCardRestoreTag = "BindingCardRestore";

	ControlActionEditor? _hotkeyCaptureItem;
	DateTime? _hotkeyCaptureDownUtc;
	HotkeyGesture _hotkeyCaptureGesture;
	bool _hotkeyCaptureAwaitingRelease;
	bool _initialPlacementDone;

	readonly MixerController _mixer;
	readonly TrayController _trayController;
	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;
	public ConfigWindowViewModel vm { get; }

	/// <summary>Fluent Expander template: <see cref="Expander"/> → HeaderSite → ChevronGrid.</summary>
	readonly Dictionary<Expander, (BindingEditor bed, PropertyChangedEventHandler handler)> _bindingCardChevronDimHooks = [];

	// Binding editor collection moved to the view model (`ConfigWindowViewModel.bindings`).

	public ConfigWindow(MixerController mixer, TrayController trayController, AppCoordinator appCoordinator, ConfigStore configStore) {
		InitializeComponent();
		DataContext = this;
		_mixer = mixer;
		_trayController = trayController;
		_appCoordinator = appCoordinator;
		_configStore = configStore;
		vm = new ConfigWindowViewModel(_mixer, _trayController, _appCoordinator, _configStore);
		vm.PropertyChanged += vm_PropertyChanged;
		loadFromConfigStore();
		syncTitlebarIconFromTray();
		WindowStartupLocation = WindowStartupLocation.Manual;
		ContentRendered += (_, _) => placeNearTrayCornerOnce();
	}

	public void syncTitlebarIconFromTray() => Icon = _trayController.windowIconSourceSnapshot;

	void placeNearTrayCornerOnce() {
		if (_initialPlacementDone)
			return;
		_initialPlacementDone = true;

		System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
		Screen screen = Screen.FromPoint(cursor);
		System.Drawing.Rectangle waPx = screen.WorkingArea;

		PresentationSource? ps = PresentationSource.FromVisual(this);
		Matrix fromDevice = ps?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

		// WinForms gives pixels; WPF window coordinates are DIPs.
		System.Windows.Point waTopLeft = fromDevice.Transform(new System.Windows.Point(waPx.Left, waPx.Top));
		System.Windows.Point waBottomRight = fromDevice.Transform(new System.Windows.Point(waPx.Right, waPx.Bottom));
		var wa = new System.Windows.Rect(waTopLeft, waBottomRight);

		const double insetDip = 24d;

		double w = ActualWidth > 0 ? ActualWidth : Width;
		double h = ActualHeight > 0 ? ActualHeight : Height;

		double left = wa.Right - w - insetDip;
		double top = wa.Bottom - h - insetDip;

		double minLeft = wa.Left + insetDip;
		double minTop = wa.Top + insetDip;
		double maxLeft = wa.Right - w - insetDip;
		double maxTop = wa.Bottom - h - insetDip;

		// If the window is larger than the working area, keep it pinned to the top-left of the working area.
		if (maxLeft < minLeft)
			Left = wa.Left;
		else
			Left = Math.Clamp(left, minLeft, maxLeft);

		if (maxTop < minTop)
			Top = wa.Top;
		else
			Top = Math.Clamp(top, minTop, maxTop);
	}

	void vm_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
		// Window owns the bottom status bar only; per-panel feedback is handled inside the UserControls.
		if (e.PropertyName is nameof(ConfigWindowViewModel.statusFeedback))
			UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
	}

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
		vm.loadFromConfigStore();
		UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
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
		vm.pingMinText = formatLatencyCellText(ping.minMs);
		vm.pingMedianText = formatLatencyCellText(ping.medianMs);
		vm.pingMaxText = formatLatencyCellText(ping.maxMs);
		vm.pingLossText = ping.completedCount == 0 ? "—" : ping.receivedCount.ToString(CultureInfo.InvariantCulture);

		vm.oscMinText = formatLatencyCellText(osc.minMs);
		vm.oscMedianText = formatLatencyCellText(osc.medianMs);
		vm.oscMaxText = formatLatencyCellText(osc.maxMs);
		vm.oscLossText = osc.completedCount == 0 ? "—" : osc.receivedCount.ToString(CultureInfo.InvariantCulture);

		int completed = ping.completedCount;
		vm.lossUnitText = "/" + completed.ToString(CultureInfo.InvariantCulture);

		vm.pingLossForeground = brushForStatus(responseStatus(ping.completedCount, ping.receivedCount));
		vm.oscLossForeground = brushForStatus(responseStatus(osc.completedCount, osc.receivedCount));

		vm.pingMinForeground = brushForStatus(latencyStatus(timeoutMs, ping.minMs));
		vm.pingMedianForeground = brushForStatus(latencyStatus(timeoutMs, ping.medianMs));
		vm.pingMaxForeground = brushForStatus(latencyStatus(timeoutMs, ping.maxMs));

		vm.oscMinForeground = brushForStatus(latencyStatus(timeoutMs, osc.minMs));
		vm.oscMedianForeground = brushForStatus(latencyStatus(timeoutMs, osc.medianMs));
		vm.oscMaxForeground = brushForStatus(latencyStatus(timeoutMs, osc.maxMs));
	}

	async void buttonApplySaveAndTest_Click(object sender, RoutedEventArgs e) {
		// Don't rely on PropertyChanged for repeating equal feedback values.
		vm.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.WARNING);
		UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
		(bool okBuild, AppConfig? newConfig, UiTextFeedback? buildErr) = SettingsFormDraft.tryBuild(
			vm.oscIpText,
			vm.oscPortText,
			vm.queryTimeoutText,
			vm.valueCacheTtlText,
			vm.osdPosition,
			vm.osdHeightText,
			vm.osdDurationText,
			vm.hotkeyLongPressMsText,
			vm.hotkeyOptimizeNonLongPress,
			vm.hotkeySuppressLongPressOnly,
			vm.hotkeyAcceptMacroChordKeyOrder,
			vm.bindings);
		if (!okBuild) {
			vm.statusFeedback = buildErr!.Value;
			UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
			return;
		}

		_appCoordinator.beginConfigValidation();
		try {
			_appCoordinator.commitConfigFromSettingsForm(newConfig!);
			vm.configFeedback = _configStore.lastDiskUiFeedback;

			int timeoutMs = Math.Max(1, (int)newConfig!.mixer.timeoutMs);
			const int probes = 10;

			var pingStats = new RttStatsAccumulator();
			var oscStats = new RttStatsAccumulator();

			applyLatencyStatsToUi(timeoutMs, pingStats.snapshot(), oscStats.snapshot());

			BindingAbstract? firstBinding = (newConfig.trayApp?.bindings ?? []).FirstOrDefault();
			bool oscUsesBinding = firstBinding != null;
			string oscAddress = oscUsesBinding ? firstBinding!.address : "/info";
			vm.oscHeaderText = oscUsesBinding ? "OSC Binding #1" : "OSC /info";
			// TODO(split-to-resources): bind this via VM once latency panel is VM-driven.

			bool lastInfoOk = false;
			string lastInfoDetail = "";

			async Task<int?> probeOscOnceAsync() {
				if (oscUsesBinding) {
					switch (firstBinding) {
						case BindingFloatAbstract:
							float? reply = await _mixer.QueryContinuousWireAsync(oscAddress);
							if (reply == null)
								return null;
							break;
						case BindingToggle:
							bool? reply2 = await _mixer.QueryToggleAsync(oscAddress);
							if (reply2 == null)
								return null;
							break;
						default:
							return null;
					}
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

			vm.infoFeedback = MixerController.infoQueryDetailFeedback(lastInfoOk, lastInfoDetail);
			vm.statusFeedback = MixerController.settingsApplyMixerSummaryFeedback(lastInfoOk);
			UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
		} catch (Exception ex) {
			vm.statusFeedback = MixerController.exceptionMessageFeedback(ex);
			UiTextFeedbackPresenter.apply(StatusTextBlock, vm.statusFeedback);
		} finally {
			_appCoordinator.finishConfigValidation();
		}
	}

	// Autostart/config-path panel moved to SettingsPanelView.

	// Binding editor actions moved to VM commands.

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

	// Hotkey editor row actions moved to VM commands.

	// Hotkey capture moved to BindingsPanelView.

	void clearHotkeyCaptureTracking() {
		_hotkeyCaptureItem = null;
		_hotkeyCaptureDownUtc = null;
		_hotkeyCaptureGesture = HotkeyGesture.None;
		_hotkeyCaptureAwaitingRelease = false;
	}

	// Hotkey capture moved to BindingsPanelView.

	void finalizeHotkeyCapture(ControlActionEditor item, FrameworkElement focusMoveAnchor) {
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
		// Hotkey capture moved to BindingsPanelView.
	}

	// Hotkey capture moved to BindingsPanelView.

	void beginHotkeyCaptureKeyDown(KeyEventArgs e, ControlActionEditor item) {
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
		if (values.Length < 3)
			return false;
		object? item = values[0];
		object? draggedItem = values[1];
		bool isDragging = values[2] is bool b && b;
		if (!isDragging)
			return false;
		// Placeholder should be at the original slot of the dragged item.
		// Comparing list identity here is brittle (templates / visual tree changes can break it),
		// but item reference equality is stable and sufficient because the dragged item instance is unique.
		return ReferenceEquals(item, draggedItem);
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
