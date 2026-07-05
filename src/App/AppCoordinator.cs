using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using WindowsOscVolumeControl.UI.Config;
using WindowsOscVolumeControl.UI.Osd;
using WindowsOscVolumeControl.UI.Tray;

namespace WindowsOscVolumeControl.Diagnostics {
	public abstract partial record StatusError {
		public abstract record Application : StatusError {
			public sealed record StartupHealthFault : Application;
		}
	}
}

namespace WindowsOscVolumeControl.App {

public sealed class AppCoordinator : IDisposable {
	readonly ConfigStore _configStore = new();
	readonly BindingManager _oscBindings = new();
	readonly StatusRegister<StatusError> _applicationStatusRegister = new();
	readonly StatusController _statusController = new();
	readonly Dispatcher _dispatcher;
	TrayController _tray;
	KeyboardHook _hook;
	OSDController _osd;
	OscTransport _transport;
	MixerController _mixer;
	ConfigWindow? _configWindow;
	StatusController.MergedState _lastMergedState = StatusController.MergedState.STARTING_OR_INVALID_CONFIG;
	bool _disposed;

	public AppCoordinator() {
		_dispatcher = System.Windows.Application.Current.Dispatcher;
		_configStore.loadFromDisk();
		X32Catalog.ensureLoaded();

		_osd = new OSDController(_configStore.appConfig.osd);
		_tray = new TrayController(openConfig, closeApp);
		_tray.setOscEndPoint(_configStore.appConfig.oscTransport.endPoint);
		_transport = new OscTransport(_configStore.appConfig.oscTransport);
		_mixer = new MixerController(_transport, _configStore.appConfig.mixer);
		_hook = new KeyboardHook();

		_statusController.attach("startupHealth", _applicationStatusRegister);
		_statusController.attach("mixerRuntime", _mixer.statusRegister);
		_statusController.attach("keyboardHook", _hook.statusRegister);
		_statusController.attach("oscTransport", _transport.statusRegister);
		_statusController.mergedStateChanged += onMergedStateChanged;
		_statusController.visibleStatusErrorsChanged += onVisibleStatusErrorsChanged;
		_mixer.eventReceived += onMixerEvent;

		_applicationStatusRegister.setStatusError<StatusError.Generic.Starting>(true);
		rebuildHotkeysFromConfig(_configStore.appConfig.trayApp.bindings);
		syncStatusUi();

		Task startupTask = runStartupHealthAsync();
		_ = startupTask.ContinueWith(t => {
			AppTrace.Application.TraceEvent(TraceEventType.Error, 0, $"Startup health failed: {t.Exception}");
			_applicationStatusRegister.setStatusError<StatusError.Application.StartupHealthFault>(true);
			_applicationStatusRegister.setStatusError<StatusError.Generic.Starting>(false);
		}, TaskContinuationOptions.OnlyOnFaulted);
	}

	void rebuildHotkeysFromConfig(IEnumerable<BindingAbstract> bindings) {
		_oscBindings.rebuildFromConfig(bindings);
		_hook.setHotkeyDispatch(
			gesture => {
				if (!_oscBindings.tryGetDispatchTargets(gesture, out HotkeyDispatchTargets t) || !t.hasAny)
					return null;
				return t;
			},
			slots => {
				foreach (BindingManager.Slot slot in slots)
					handleOscHotkey(slot.binding, slot.action);
			},
			_oscBindings.boundMainKeyCodes);
		_hook.applyConfig(_configStore.appConfig.keyboardHook);
	}

	public void setConfiguredHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

	public void beginConfigValidation() {
		_applicationStatusRegister.setStatusError<StatusError.Application.StartupHealthFault>(false);
		_applicationStatusRegister.setStatusError<StatusError.Generic.Starting>(true);
	}

	public void finishConfigValidation() =>
		_applicationStatusRegister.setStatusError<StatusError.Generic.Starting>(false);

	internal string visibleDiagnosticsSummaryForConfigUi() =>
		VisibleDiagnosticsFormatting.formatVisibleStatusErrors(_statusController.getVisibleStatusErrorTypes());

	public async Task applyConfigFromStoreAsync() {
		AppConfig cfg = _configStore.appConfig;
		// Socket teardown/rebind may briefly block on the old receive loop; await keeps the UI thread responsive.
		// No ConfigureAwait(false): the remainder touches WPF-affine objects and must resume on the dispatcher.
		await _transport.applyConfigAsync(cfg.oscTransport);
		_tray.setOscEndPoint(cfg.oscTransport.endPoint);
		_mixer.ApplyConfig(cfg.mixer);
		_osd.ApplyConfig(cfg.osd);
		rebuildHotkeysFromConfig(cfg.trayApp.bindings);
	}

	public async Task commitConfigFromSettingsFormAsync(AppConfig newConfig) {
		ArgumentNullException.ThrowIfNull(newConfig);
		_configStore.adoptAppConfig(newConfig);
		await applyConfigFromStoreAsync();
		_configStore.tryPersistToDisk();
	}

	async Task runStartupHealthAsync() {
		await _mixer.TestConnectionAsync().ConfigureAwait(false);
		_applicationStatusRegister.setStatusError<StatusError.Application.StartupHealthFault>(false);
		_applicationStatusRegister.setStatusError<StatusError.Generic.Starting>(false);
	}

	void onMergedStateChanged(StatusController.MergedState state) {
		ui(() => {
			bool enteringNetworkError = state == StatusController.MergedState.NETWORK_ERROR
				&& _lastMergedState != StatusController.MergedState.NETWORK_ERROR;
			_lastMergedState = state;
			_tray.ApplyState(mapMergedState(state));
			if (enteringNetworkError)
				_osd.ShowError();
			_configWindow?.syncTitlebarIconFromTray();
		});
	}

	void onVisibleStatusErrorsChanged() {
		string summary = VisibleDiagnosticsFormatting.formatVisibleStatusErrors(_statusController.getVisibleStatusErrorTypes());
		ui(() => {
			_tray.setStatusText(summary);
			_configWindow?.syncDiagnosticsFeedback(summary);
		});
	}

	void syncStatusUi() {
		_lastMergedState = _statusController.getMergedState();
		_tray.ApplyState(mapMergedState(_lastMergedState));
		_tray.setStatusText(VisibleDiagnosticsFormatting.formatVisibleStatusErrors(_statusController.getVisibleStatusErrorTypes()));
	}

	static AppTrayIconState mapMergedState(StatusController.MergedState state) => state switch {
		StatusController.MergedState.OK => AppTrayIconState.OK,
		StatusController.MergedState.NETWORK_ERROR => AppTrayIconState.NETWORK_ERROR,
		_ => AppTrayIconState.STARTING_OR_INVALID_CONFIG,
	};

	void onMixerEvent(MixerController.Event evt) {
		ui(() => {
			switch (evt) {
				case MixerController.Event.FaderChanged f when tryGetFloatBinding(f.address, out BindingFloatAbstract? bf):
					float ratio = bf.getNormalizedRatio(f.newLevel);
					string display = formatContinuousDisplay(f.newLevel, bf);
					_osd.ShowLevel(bf.displayName, ratio, display, f.volumeIncreased);
					break;
				case MixerController.Event.ToggleChanged t when tryGetToggleBinding(t.address, out BindingToggle? toggleBinding):
					_osd.ShowToggle(toggleBinding.displayName, t.nowOn);
					break;
				case MixerController.Event.OperationFailed:
					_osd.ShowError();
					break;
			}
		});
	}

	bool tryGetFloatBinding(string address, [NotNullWhen(true)] out BindingFloatAbstract? binding) =>
		_oscBindings.tryGetFloatBindingByAddress(address, out binding);

	bool tryGetToggleBinding(string address, [NotNullWhen(true)] out BindingToggle? binding) =>
		_oscBindings.tryGetToggleBindingByAddress(address, out binding);

	static string formatContinuousDisplay(float wire, BindingFloatAbstract bf) {
		if (bf is BindingLevel bl) {
			if (wire <= 0f)
				return "-∞ dB";
			float db = bl.toReal(wire);
			return ContinuousFloatUtil.FormatOsdLevelValue(db, bf.osdFractionalDigits) + " dB";
		}
		if (bf is BindingFloatNormalizedAbstract n) {
			float real = n.toReal(wire);
			string core = ContinuousFloatUtil.FormatOsdLevelValue(real, bf.osdFractionalDigits);
			return bf.unit is { } u ? core + " " + u : core;
		}
		string c = ContinuousFloatUtil.FormatOsdLevelValue(wire, bf.osdFractionalDigits);
		return bf.unit is { } u2 ? c + " " + u2 : c;
	}

	void openConfig() {
		ui(() => {
			if (_configWindow != null) {
				if (_configWindow.WindowState == WindowState.Minimized)
					_configWindow.WindowState = WindowState.Normal;
				_configWindow.Activate();
				return;
			}

			_configWindow = new ConfigWindow(_mixer, _tray, this, _configStore);
			_configWindow.syncDiagnosticsFeedback(
				VisibleDiagnosticsFormatting.formatVisibleStatusErrors(_statusController.getVisibleStatusErrorTypes()));
			_configWindow.Closed += (_, _) => {
				setConfiguredHotkeysEnabled(true);
				_configWindow?.syncDiagnosticsFeedback("");
				_configWindow = null;
				scheduleGcTrimAfterConfigClosed();
			};
			_configWindow.Show();
			_configWindow.Activate();
		});
	}

	void scheduleGcTrimAfterConfigClosed() {
		if (_disposed)
			return;
		// Wait for the dispatcher to go idle first: pending render/teardown work can still reference
		// the closed window, and collecting too early would miss that garbage.
		_ = _dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(runGcTrimInBackground));
	}

	void runGcTrimInBackground() {
		if (_disposed)
			return;
		_ = Task.Run(() => {
			try {
				if (_disposed)
					return;
				using Process proc = Process.GetCurrentProcess();
				long managedBefore = GC.GetTotalMemory(false);
				long wsBefore = proc.WorkingSet64;
				long privBefore = proc.PrivateMemorySize64;

				GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, blocking: true, compacting: true);
				GC.WaitForPendingFinalizers();
				GC.Collect(GC.MaxGeneration, GCCollectionMode.Default, blocking: true, compacting: true);

#if !DEBUG
				ProcessWorkingSetTrim.tryTrimWorkingSet();
#endif
				proc.Refresh();
				long managedAfter = GC.GetTotalMemory(false);
				long wsAfter = proc.WorkingSet64;
				long privAfter = proc.PrivateMemorySize64;
				AppTrace.Application.TraceEvent(
					TraceEventType.Information,
					0,
					$"Post-settings memory trim: managed {managedBefore}->{managedAfter} B, WorkingSet {wsBefore}->{wsAfter} B, PrivateBytes {privBefore}->{privAfter} B");
			} catch (Exception ex) {
				AppTrace.Application.TraceEvent(TraceEventType.Warning, 0, "Post-settings memory trim failed: " + ex);
			}
		});
	}

	void ui(Action action) {
		if (_disposed)
			return;
		if (_dispatcher.CheckAccess()) {
			action();
			return;
		}
		_ = _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
	}

	void handleOscHotkey(BindingAbstract binding, ControlAction action) {
		string display = binding.displayName;
		switch (action) {
			case ControlActionContinuousAbstract ca when binding is BindingFloatAbstract bf:
				if (ca.needsCurrentWire && !_mixer.HasFreshContinuousSample(bf.address))
					ui(() => _osd.ShowPending(display));
				_mixer.enqueueContinuousAction(bf.address, ca, bf);
				break;
			case ControlActionToggleSet ts when binding is BindingToggle toggle:
				ui(() => _osd.ShowPending(display));
				_mixer.setToggle(toggle.address, ts.on);
				break;
			case ControlActionToggleFlip when binding is BindingToggle toggle2:
				ui(() => _osd.ShowPending(display));
				_mixer.toggle(toggle2.address);
				break;
		}
	}

	void closeApp() {
		if (_disposed)
			return;

		_disposed = true;
		_hook.Dispose();
		_transport.Dispose();
		_tray.Dispose();
		_configWindow?.Close();
		_osd.Close();
		System.Windows.Application.Current.Shutdown();
	}

	public void Dispose() => closeApp();
}
}
