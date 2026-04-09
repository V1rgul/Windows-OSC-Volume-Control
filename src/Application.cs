using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

public abstract partial record Error {
	public abstract partial record Application : Error {
		public sealed record StartupHealthFault : Application;
	}
}

public class Application : ApplicationContext {
	readonly ConfigStore _configStore = new();
	readonly ResourceLoader _resources = new();
	readonly BindingManager _oscBindings = new();
	readonly ErrorList<Error> _applicationErrors = new();
	readonly StatusController _statusController = new();
	TrayController _tray;
	KeyboardHook _hook;
	OSDController _osd;
	OscTransport _transport;
	MixerController _mixer;
	ConfigForm? _configForm;
	StatusController.MergedState _lastMergedState = StatusController.MergedState.STARTING_OR_INVALID_CONFIG;

	public Application() {
		_configStore.loadFromDisk();

		_osd = new OSDController(_configStore.appConfig.osd);
		_ = _osd.Handle;

		_tray = new TrayController(_resources, openConfig, closeApp);
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
		rebuildHotkeysFromConfig(_configStore.appConfig.trayApp?.faderBindings ?? [], _configStore.appConfig.trayApp?.bindings ?? []);
		syncStatusUi();

		Task startupTask = runStartupHealthAsync();
		_ = startupTask.ContinueWith(t => {
			AppTrace.Application.TraceEvent(TraceEventType.Error, 0, $"Startup health failed: {t.Exception}");
			_applicationErrors.setError(new Error.Application.StartupHealthFault(), true);
			_applicationErrors.setError(new Error.Generic.Starting(), false);
		}, TaskContinuationOptions.OnlyOnFaulted);
	}

	void rebuildHotkeysFromConfig(IEnumerable<BindingFader> faders, IEnumerable<BindingToggle> toggles) {
		_oscBindings.rebuildFromConfig(faders, toggles);
		_hook.setKeyCallback(k => {
			if (!_oscBindings.tryGetSlot(k, out BindingManager.Slot slot))
				return null;
			return () => handleOscHotkey(slot.binding, slot.kind);
		});
	}

	internal void setOscToggleHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

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
		rebuildHotkeysFromConfig(cfg.trayApp?.faderBindings ?? [], cfg.trayApp?.bindings ?? []);
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
			if (_configForm != null && !_configForm.IsDisposed)
				_configForm.syncTitlebarIconFromTray();
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
					_osd.ShowLevel(binding.displayName, binding.minimum, binding.maximum, f.newLevel, f.volumeIncreased, binding.step);
					break;
				case MixerController.Event.ToggleChanged t when tryGetToggleBinding(t.address, out BindingToggle? binding):
					_osd.ShowToggle(binding.displayName, t.nowOn);
					break;
				case MixerController.Event.OperationFailed:
					_osd.ShowError();
					break;
			}
		});
	}

	bool tryGetFaderBinding(string address, [NotNullWhen(true)] out BindingFader? binding) {
		binding = _configStore.appConfig.trayApp?.faderBindings.FirstOrDefault(f => string.Equals(f.address, address, StringComparison.Ordinal));
		return binding != null;
	}

	bool tryGetToggleBinding(string address, [NotNullWhen(true)] out BindingToggle? binding) {
		binding = _configStore.appConfig.trayApp?.bindings.FirstOrDefault(t => string.Equals(t.address, address, StringComparison.Ordinal));
		return binding != null;
	}

	void openConfig() {
		if (_configForm != null && !_configForm.IsDisposed) {
			if (_configForm.WindowState == FormWindowState.Minimized)
				_configForm.WindowState = FormWindowState.Normal;
			_configForm.Activate();
			return;
		}

		_configForm = new ConfigForm(_mixer, _tray, this, _configStore, _resources);
		_configForm.FormClosed += (_, _) => _configForm = null;
		_configForm.Show();
	}

	void ui(Action action) {
		if (_osd.IsDisposed)
			return;

		void run() {
			if (_osd.IsDisposed)
				return;
			action();
		}

		if (_osd.IsHandleCreated && _osd.InvokeRequired)
			_osd.BeginInvoke(run);
		else
			run();
	}

	void handleOscHotkey(BindingAbstract binding, BindingManager.Slot.Kind kind) {
		string display = binding.displayName;
		if (kind == BindingManager.Slot.Kind.TOGGLE) {
			var toggleBinding = (BindingToggle)binding;
			ui(() => _osd.ShowPending(display));
			_mixer.toggle(toggleBinding.address);
			return;
		}

		if (kind is BindingManager.Slot.Kind.UP or BindingManager.Slot.Kind.DOWN) {
			var faderBinding = (BindingFader)binding;
			if (!_mixer.HasFreshFaderSample(faderBinding.address))
				ui(() => _osd.ShowPending(display, faderBinding.step));
			float delta = kind == BindingManager.Slot.Kind.UP ? faderBinding.step : -faderBinding.step;
			_mixer.nudge(faderBinding.address, delta, faderBinding.minimum, faderBinding.maximum);
		}
	}

	void closeApp() {
		_hook.Dispose();
		_transport.Dispose();
		_osd.Close();
		_tray.Dispose();
		System.Windows.Forms.Application.Exit();
	}
}
