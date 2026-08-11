using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Result;
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
	DispatcherTimer? _applySpinnerTimer;
	RotateTransform? _applySpinnerRotate;

	enum ApplyButtonChrome {
		IDLE,
		BUSY,
		FLASH_OK,
		FLASH_FAIL,
	}

	ApplyButtonChrome _applyButtonChrome = ApplyButtonChrome.IDLE;
	public ConfigWindowViewModel vm { get; }

	public ConfigWindow(MixerController mixer, TrayController trayController, AppCoordinator appCoordinator, ConfigStore configStore) {
		InitializeComponent();
		_mixer = mixer;
		_trayController = trayController;
		_appCoordinator = appCoordinator;
		_configStore = configStore;
		vm = new ConfigWindowViewModel(_appCoordinator, _configStore);
		vm.PropertyChanged += vm_PropertyChanged;
		DataContext = this;
		loadFromConfigStore();
		syncTitlebarIconFromTray();
		WindowStartupLocation = WindowStartupLocation.Manual;
		ContentRendered += (_, _) => placeNearTrayCornerOnce();
		Loaded += (_, _) => {
			applyFooterChromeLikeDraggedCard();
			syncApplyButtonTextBrush();
		};
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
		if (e.PropertyName is nameof(ConfigWindowViewModel.statusFeedback) or nameof(ConfigWindowViewModel.diagnosticsFeedback))
			refreshStatusBar();
		if (e.PropertyName is nameof(ConfigWindowViewModel.hasScalarErrors) && _applyButtonChrome == ApplyButtonChrome.IDLE)
			syncApplyButtonTextBrush();
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

	Media.Brush applyButtonAccentTextBrush() =>
		tryGetThemeBrush("TextOnAccentFillColorPrimaryBrush", Media.Brushes.White);

	Media.Brush applyButtonCriticalBrush() =>
		tryGetThemeBrush("SystemFillColorCriticalBrush", Media.Brushes.IndianRed);

	Media.Brush applyButtonSuccessBrush() =>
		tryGetThemeBrush("SystemFillColorSuccessBrush", Media.Brushes.LimeGreen);

	void setApplyButtonBusy(bool busy) {
		ApplySaveAndTestSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
		if (busy)
			startApplySpinner();
		else
			stopApplySpinner();
		if (busy)
			_applyButtonChrome = ApplyButtonChrome.BUSY;
		else if (_applyButtonChrome == ApplyButtonChrome.BUSY)
			_applyButtonChrome = ApplyButtonChrome.IDLE;
		syncApplyButtonTextBrush();
	}

	void startApplySpinner() {
		_applySpinnerRotate ??= (RotateTransform)ApplySaveAndTestSpinner.RenderTransform;
		if (_applySpinnerTimer == null) {
			_applySpinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
			_applySpinnerTimer.Tick += (_, _) => {
				if (_applySpinnerRotate != null)
					_applySpinnerRotate.Angle = (_applySpinnerRotate.Angle + 12d) % 360d;
			};
		}
		_applySpinnerTimer.Start();
	}

	void stopApplySpinner() {
		_applySpinnerTimer?.Stop();
		if (_applySpinnerRotate != null)
			_applySpinnerRotate.Angle = 0d;
	}

	void syncApplyButtonTextBrush() {
		ApplySaveAndTestButtonText.Foreground = vm.hasScalarErrors
			? applyButtonCriticalBrush()
			: applyButtonAccentTextBrush();
	}

	async void buttonApplySaveAndTest_Click(object sender, RoutedEventArgs e) {
		if (_applySaveAndTestRunning)
			return;

		_applySaveAndTestRunning = true;
		vm.applyInProgress = true;
		setApplyButtonBusy(busy: true);
		await Dispatcher.Yield(DispatcherPriority.Render);
		bool? flashSuccess = null;
		bool lastInfoOk = false;
		try {
			if (vm.hasScalarErrors) {
				vm.statusFeedback = new UiTextFeedback(SettingsPanel.formatScalarErrorsForFooter(vm), UiTextFeedbackKind.ERROR);
				refreshStatusBar();
				flashSuccess = false;
				return;
			}

			if (!vm.tryBuildAppConfig(out AppConfig? newConfig, out UiTextFeedback? buildErr)) {
				vm.statusFeedback = buildErr!.Value;
				refreshStatusBar();
				flashSuccess = false;
				return;
			}

			vm.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
			refreshStatusBar();
			_appCoordinator.beginConfigValidation();
			try {
				await _appCoordinator.commitConfigFromSettingsFormAsync(newConfig!);
				vm.configFeedback = _configStore.lastDiskUiFeedback;
				if (_configStore.lastDiskOutcome is AppConfigDiskOutcome.SAVE_FAILED) {
					vm.statusFeedback = vm.configFeedback;
					refreshStatusBar();
					flashSuccess = false;
					return;
				}

				int timeoutMs = Math.Max(1, (int)newConfig!.mixer.timeoutMs);
				const int probes = 10;

				var pingStats = new RttStatsAccumulator();
				var oscStats = new RttStatsAccumulator();

				vm.applyLatencyStatsToUi(timeoutMs, pingStats.snapshot(), oscStats.snapshot(), brushForLatencyPanel);

				BindingAbstract? firstBinding = newConfig.trayApp.bindings.FirstOrDefault();
				bool oscUsesBinding = firstBinding != null;
				string oscAddress = oscUsesBinding ? firstBinding!.address : "/info";
				vm.oscHeaderText = oscUsesBinding ? "OSC Binding #1" : "OSC /info";

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
					await Dispatcher.Yield(DispatcherPriority.Render);
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

			bool diskClean = vm.configFeedback.kind is UiTextFeedbackKind.SUCCESS or UiTextFeedbackKind.DEFAULT;
			bool diagnosticsClean = vm.diagnosticsFeedback.kind is UiTextFeedbackKind.SUCCESS or UiTextFeedbackKind.DEFAULT;
			flashSuccess = lastInfoOk && diskClean && diagnosticsClean;
		} finally {
			vm.applyInProgress = false;
			_applySaveAndTestRunning = false;
			setApplyButtonBusy(busy: false);
			if (flashSuccess is bool success)
				flashApplyButtonChrome(success);
			else
				syncApplyButtonTextBrush();
		}
	}

	void flashApplyButtonChrome(bool success) {
		_applyButtonChrome = success ? ApplyButtonChrome.FLASH_OK : ApplyButtonChrome.FLASH_FAIL;
		syncApplyButtonTextBrush();

		Media.Color flashColor = colorFromBrush(
			success ? applyButtonSuccessBrush() : applyButtonCriticalBrush(),
			success ? Media.Colors.LimeGreen : Media.Colors.IndianRed);
		Media.Color accentColor = colorFromBrush(
			tryGetThemeBrush("AccentFillColorDefaultBrush", Media.Brushes.DodgerBlue),
			Media.Colors.DodgerBlue);

		var brush = new SolidColorBrush(flashColor);
		ApplySaveAndTestButton.Background = brush;

		var fade = new ColorAnimation(flashColor, accentColor, TimeSpan.FromMilliseconds(1000)) {
			EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
			FillBehavior = FillBehavior.HoldEnd,
		};
		fade.Completed += (_, _) => {
			_applyButtonChrome = ApplyButtonChrome.IDLE;
			ApplySaveAndTestButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
			syncApplyButtonTextBrush();
		};
		brush.BeginAnimation(SolidColorBrush.ColorProperty, fade);
	}

	static Media.Color colorFromBrush(Media.Brush? brush, Media.Color fallback) =>
		brush is SolidColorBrush solid ? solid.Color : fallback;

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
