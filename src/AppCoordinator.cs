using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;

namespace WindowsOscVolumeControl;

public abstract partial record Error {
	public abstract partial record Application : Error {
		public sealed record StartupHealthFault : Application;
	}
}

public sealed class AppCoordinator : IDisposable {
	readonly ConfigStore _configStore = new();
	readonly BindingManager _oscBindings = new();
	readonly ErrorList<Error> _applicationErrors = new();
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

		_osd = new OSDController(_configStore.appConfig.osd);
		_tray = new TrayController(openConfig, closeApp);
		_transport = new OscTransport(_configStore.appConfig.oscTransport);
		_mixer = new MixerController(_transport, _configStore.appConfig.mixer);
		_hook = new KeyboardHook();

		_statusController.attach("startupHealth", _applicationErrors);
		_statusController.attach("mixerRuntime", _mixer.errors);
		_statusController.attach("keyboardHook", _hook.errors);
		_statusController.mergedStateChanged += onMergedStateChanged;
		_statusController.visibleErrorsChanged += onVisibleErrorsChanged;
		_mixer.eventReceived += onMixerEvent;

		_applicationErrors.setError(new Error.Generic.Starting(), true);
		rebuildHotkeysFromConfig(_configStore.appConfig.trayApp?.bindings ?? []);
		syncStatusUi();

		Task startupTask = runStartupHealthAsync();
		_ = startupTask.ContinueWith(t => {
			AppTrace.Application.TraceEvent(TraceEventType.Error, 0, $"Startup health failed: {t.Exception}");
			_applicationErrors.setError(new Error.Application.StartupHealthFault(), true);
			_applicationErrors.setError(new Error.Generic.Starting(), false);
		}, TaskContinuationOptions.OnlyOnFaulted);
	}

	void rebuildHotkeysFromConfig(IEnumerable<BindingAbstract> bindings) {
		_oscBindings.rebuildFromConfig(bindings);
		BindingManager.Config tray = _configStore.appConfig.trayApp ?? new BindingManager.Config();
		uint longPressMs = BindingManager.Config.clampLongPressDurationMs(tray.longPressDurationMs);
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
			longPressMs,
			tray.optimizeNonLongPressKeyDown);
	}

	public void setConfiguredHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

	public void beginConfigValidation() {
		_applicationErrors.setError(new Error.Application.StartupHealthFault(), false);
		_applicationErrors.setError(new Error.Generic.Starting(), true);
	}

	public void finishConfigValidation() =>
		_applicationErrors.setError(new Error.Generic.Starting(), false);

	public void applyConfigFromStore() {
		AppConfig cfg = _configStore.appConfig;
		_transport.applyConfig(cfg.oscTransport);
		_mixer.ApplyConfig(cfg.mixer);
		_osd.ApplyConfig(cfg.osd);
		rebuildHotkeysFromConfig(cfg.trayApp?.bindings ?? []);
	}

	public void commitConfigFromSettingsForm(AppConfig newConfig) {
		ArgumentNullException.ThrowIfNull(newConfig);
		_configStore.adoptAppConfig(newConfig);
		applyConfigFromStore();
		_configStore.tryPersistToDisk();
	}

	async Task runStartupHealthAsync() {
		await _mixer.TestConnectionAsync().ConfigureAwait(false);
		_applicationErrors.setError(new Error.Application.StartupHealthFault(), false);
		_applicationErrors.setError(new Error.Generic.Starting(), false);
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

	void onVisibleErrorsChanged() {
		string summary = formatVisibleErrors(_statusController.getVisibleErrors());
		ui(() => _tray.setStatusText(summary));
	}

	void syncStatusUi() {
		_lastMergedState = _statusController.getMergedState();
		_tray.ApplyState(mapMergedState(_lastMergedState));
		_tray.setStatusText(formatVisibleErrors(_statusController.getVisibleErrors()));
	}

	static AppTrayIconState mapMergedState(StatusController.MergedState state) => state switch {
		StatusController.MergedState.OK => AppTrayIconState.OK,
		StatusController.MergedState.NETWORK_ERROR => AppTrayIconState.NETWORK_ERROR,
		_ => AppTrayIconState.STARTING_OR_INVALID_CONFIG,
	};

	static string formatVisibleErrors(IReadOnlyCollection<Error> errors) {
		if (errors.Count == 0)
			return "";

		return string.Join("; ", errors.Select(static error => error switch {
			Error.Generic.Starting => "Starting",
			Error.MixerController.Network => "Mixer network error",
			Error.MixerController.InvalidReply => "Mixer invalid reply",
			Error.KeyboardHook.InstallFailed => "Keyboard hook install failed",
			Error.Application.StartupHealthFault => "Startup health fault",
			_ => error.GetType().Name,
		}));
	}

	void onMixerEvent(MixerController.Event evt) {
		ui(() => {
			switch (evt) {
				case MixerController.Event.FaderChanged f when tryGetFaderBinding(f.address, out BindingFader? binding):
					_osd.ShowLevel(binding.displayName, binding.minimum, binding.maximum, f.newLevel, f.volumeIncreased, guessFaderStepForOsd(binding));
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

	bool tryGetFaderBinding(string address, [NotNullWhen(true)] out BindingFader? binding) {
		binding = _configStore.appConfig.trayApp?.bindings.OfType<BindingFader>()
			.FirstOrDefault(f => string.Equals(f.address, address, StringComparison.Ordinal));
		return binding != null;
	}

	bool tryGetToggleBinding(string address, [NotNullWhen(true)] out BindingToggle? binding) {
		binding = _configStore.appConfig.trayApp?.bindings.OfType<BindingToggle>()
			.FirstOrDefault(t => string.Equals(t.address, address, StringComparison.Ordinal));
		return binding != null;
	}

	static float guessFaderStepForOsd(BindingFader f) {
		foreach (HotkeyAction ha in f.hotkeys) {
			if (ha is HotkeyActionFaderDelta d && d.delta != 0f)
				return Math.Abs(d.delta);
		}
		return 0.02f;
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
			_configWindow.Closed += (_, _) => {
				setConfiguredHotkeysEnabled(true);
				_configWindow = null;
			};
			_configWindow.Show();
			_configWindow.Activate();
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

	void handleOscHotkey(BindingAbstract binding, HotkeyAction action) {
		string display = binding.displayName;
		switch (action) {
			case HotkeyActionFaderSet fs when binding is BindingFader fader:
				_mixer.setFader(fader.address, fs.value, fader.minimum, fader.maximum);
				break;
			case HotkeyActionFaderDelta fd when binding is BindingFader fader2:
				if (!_mixer.HasFreshFaderSample(fader2.address))
					ui(() => _osd.ShowPending(display, Math.Abs(fd.delta)));
				_mixer.nudge(fader2.address, fd.delta, fader2.minimum, fader2.maximum);
				break;
			case HotkeyActionToggleSet ts when binding is BindingToggle toggle:
				ui(() => _osd.ShowPending(display));
				_mixer.setToggle(toggle.address, ts.on);
				break;
			case HotkeyActionToggleFlip when binding is BindingToggle toggle2:
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
