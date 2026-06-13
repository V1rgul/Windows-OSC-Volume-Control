using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Tray;
using WindowsOscVolumeControl.UI.Wpf.Behaviors;
using WindowsOscVolumeControl.UI.Wpf.Theme;
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
	bool _applySaveAndTestRunning;
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
		Loaded += (_, _) => applyFooterChromeLikeDraggedCard();
	}

	// Match a dragged binding card: stack the card tints over the opaque window surface, then apply the
	// same ghost opacity. Resolving the surface brush needs a live visual tree, hence Loaded (not ctor).
	void applyFooterChromeLikeDraggedCard() {
		FooterSurfaceBase.Background = ThemeSurface.resolveOpaqueWindowSurfaceBrush(this);
		FooterChrome.Opacity = ReorderDragDrop.dragGhostOpacity;
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
		if (e.PropertyName is nameof(ConfigWindowViewModel.statusFeedback) or nameof(ConfigWindowViewModel.diagnosticsFeedback))
			refreshStatusBar();
	}

	internal void syncDiagnosticsFeedback(string summary) {
		vm.diagnosticsFeedback = string.IsNullOrEmpty(summary)
			? new UiTextFeedback("", UiTextFeedbackKind.DEFAULT)
			: new UiTextFeedback(summary, UiTextFeedbackKind.WARNING);
		refreshStatusBar();
	}

	void refreshStatusBar() {
		string diag = vm.diagnosticsFeedback.text.Trim();
		string status = vm.statusFeedback.text.Trim();
		string combinedText;
		if (diag.Length > 0 && status.Length > 0)
			combinedText = diag + Environment.NewLine + status;
		else if (diag.Length > 0)
			combinedText = vm.diagnosticsFeedback.text;
		else
			combinedText = vm.statusFeedback.text;

		UiTextFeedback mergedKind = UiTextFeedbackPresenter.mergeWithWorstKind(vm.statusFeedback, vm.diagnosticsFeedback);
		UiTextFeedback line = new(combinedText, mergedKind.kind);
		UiTextFeedbackPresenter.apply(StatusTextBlock, line);
	}

	void loadFromConfigStore() {
		vm.loadFromConfigStore();
		refreshStatusBar();
	}

	Media.Brush tryGetThemeBrush(string key, Media.Brush fallback) =>
		TryFindResource(key) as Media.Brush ?? fallback;

	Media.Brush brushForLatencyPanel(LatencyPanelUiStatus status) => status switch {
		LatencyPanelUiStatus.MUTED => tryGetThemeBrush("TextFillColorSecondaryBrush", Media.Brushes.Gray),
		LatencyPanelUiStatus.SUCCESS => tryGetThemeBrush("SystemFillColorSuccessBrush", Media.Brushes.LimeGreen),
		LatencyPanelUiStatus.CAUTION => tryGetThemeBrush("SystemFillColorCautionBrush", Media.Brushes.DarkOrange),
		LatencyPanelUiStatus.CRITICAL => tryGetThemeBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed),
		_ => tryGetThemeBrush("TextFillColorPrimaryBrush", Media.Brushes.White),
	};

	async void buttonApplySaveAndTest_Click(object sender, RoutedEventArgs e) {
		if (_applySaveAndTestRunning)
			return;

		_applySaveAndTestRunning = true;
		System.Windows.Controls.Button? applySaveAndTestButton = sender as System.Windows.Controls.Button;
		if (applySaveAndTestButton != null)
			applySaveAndTestButton.IsEnabled = false;
		try {
			// Don't rely on PropertyChanged for repeating equal feedback values.
			vm.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.WARNING);
			refreshStatusBar();
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
				refreshStatusBar();
				return;
			}

			_appCoordinator.beginConfigValidation();
			try {
				await _appCoordinator.commitConfigFromSettingsFormAsync(newConfig!);
				vm.configFeedback = _configStore.lastDiskUiFeedback;

				int timeoutMs = Math.Max(1, (int)newConfig!.mixer.timeoutMs);
				const int probes = 10;

				var pingStats = new RttStatsAccumulator();
				var oscStats = new RttStatsAccumulator();

				vm.applyLatencyStatsToUi(timeoutMs, pingStats.snapshot(), oscStats.snapshot(), brushForLatencyPanel);

				BindingAbstract? firstBinding = newConfig.trayApp.bindings.FirstOrDefault();
				bool oscUsesBinding = firstBinding != null;
				string oscAddress = oscUsesBinding ? firstBinding!.address : "/info";
				vm.oscHeaderText = oscUsesBinding ? "OSC Binding #1" : "OSC /info";

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
					vm.applyLatencyStatsToUi(timeoutMs, pingSnap, oscSnap, brushForLatencyPanel);
				}

				if (oscUsesBinding) {
					(lastInfoOk, lastInfoDetail) = await _mixer.QueryInfoAsync();
				}

				vm.infoFeedback = SettingsFeedback.infoQueryDetail(lastInfoOk, lastInfoDetail);
				vm.statusFeedback = SettingsFeedback.settingsApplyMixerSummary(lastInfoOk);
				refreshStatusBar();
			} catch (Exception ex) {
				vm.statusFeedback = SettingsFeedback.exceptionMessage(ex);
				refreshStatusBar();
			} finally {
				_appCoordinator.finishConfigValidation();
			}
		} finally {
			if (applySaveAndTestButton != null)
				applySaveAndTestButton.IsEnabled = true;
			_applySaveAndTestRunning = false;
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
