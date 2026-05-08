using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Tray;
using AppCoordinator = WindowsOscVolumeControl.App.AppCoordinator;
using Media = System.Windows.Media;

namespace WindowsOscVolumeControl.UI.Config;

public partial class ConfigWindow {
	public const string HotkeyAssignmentCaptureTag = "HotkeyAssignmentCapture";
	public const string BindingCardRestoreTag = "BindingCardRestore";

	bool _initialPlacementDone;

	readonly MixerController _mixer;
	readonly TrayController _trayController;
	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;
	public ConfigWindowViewModel vm { get; }

	public ConfigWindow(MixerController mixer, TrayController trayController, AppCoordinator appCoordinator, ConfigStore configStore) {
		InitializeComponent();
		DataContext = this;
		_mixer = mixer;
		_trayController = trayController;
		_appCoordinator = appCoordinator;
		_configStore = configStore;
		vm = new ConfigWindowViewModel(_appCoordinator, _configStore);
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

		var cursor = Control.MousePosition;
		Screen screen = Screen.FromPoint(cursor);
		Rectangle waPx = screen.WorkingArea;

		PresentationSource? ps = PresentationSource.FromVisual(this);
		Matrix fromDevice = ps?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

		// WinForms gives pixels; WPF window coordinates are DIPs.
		System.Windows.Point waTopLeft = fromDevice.Transform(new System.Windows.Point(waPx.Left, waPx.Top));
		System.Windows.Point waBottomRight = fromDevice.Transform(new System.Windows.Point(waPx.Right, waPx.Bottom));
		var wa = new Rect(waTopLeft, waBottomRight);

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

	Media.Brush tryGetThemeBrush(string key, Media.Brush fallback) =>
		TryFindResource(key) as Media.Brush ?? fallback;

	Media.Brush brushForStatus(ConfigUiStatus status) => status switch {
		ConfigUiStatus.MUTED => tryGetThemeBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray),
		// Prefer system/theme success; fall back to the more-legible green.
		ConfigUiStatus.SUCCESS => tryGetThemeBrush("SystemFillColorSuccessBrush", Media.Brushes.LimeGreen),
		ConfigUiStatus.CAUTION => tryGetThemeBrush("SystemFillColorCautionBrush", Media.Brushes.DarkOrange),
		ConfigUiStatus.CRITICAL => tryGetThemeBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
		_ => tryGetThemeBrush("TextFillColorPrimaryBrush", Media.Brushes.White),
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

			BindingAbstract? firstBinding = newConfig.trayApp.bindings.FirstOrDefault();
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

}

public sealed class DragActivePlaceholderMultiConverter : IMultiValueConverter {
	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
		if (values.Length < 3)
			return false;
		object item = values[0];
		object draggedItem = values[1];
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
