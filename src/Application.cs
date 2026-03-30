using System.Collections.Generic;
using System.Net;
using System.Windows.Forms;
using WindowsOscVolumeControl;


public class Application : ApplicationContext
{
	readonly ConfigStore _configStore = new();
	readonly ResourceLoader _resources = new();
	private TrayController _tray;

	private KeyboardHook _hook;
	private OSDController _osd;
	private MixerController _mixer;
	private ConfigForm? _configForm;
	readonly BindingManager _oscBindings = new();



	void rebuildHotkeysFromConfig(IEnumerable<BindingFader> faders, IEnumerable<BindingToggle> toggles) {
		_oscBindings.rebuildFromConfig(faders, toggles);
		_hook.setKeyCallback(k => {
			if (!_oscBindings.tryGetSlot(k, out BindingManager.Slot slot))
				return null;
			return () => _ = handleOscHotkeyAsync(slot.binding, slot.kind);
		});
	}

	internal void setOscToggleHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

	internal Icon applyTrayIconState(AppTrayIconState state, bool showErrorOsdIfNotOk = false) {
		_tray.ApplyState(state);
		if (showErrorOsdIfNotOk && state != AppTrayIconState.OK)
			_osd.ShowError();
		return _tray.TrayIconSnapshot;
	}

	public void applyConfigFromStore() {
		AppConfig cfg = _configStore.appConfig;
		_mixer.ClearFaderSampleCache();
		_mixer.Osc.ApplyConfig(cfg.oscController);
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

	public Application() {
		_configStore.loadFromDisk();

		_mixer = new MixerController(new OscController(_configStore.appConfig.oscController), _configStore.appConfig.mixer);

		_osd = new OSDController(_configStore.appConfig.osd);
		// Reading Control.Handle forces WinForms to create the native window (HWND) for this form on the current thread. Until that exists, InvokeRequired is not meaningful and ui() could run OSD updates on a background thread.
		_ = _osd.Handle;

		_tray = new TrayController(_resources, openConfig, closeApp);

		_hook = new KeyboardHook();

		rebuildHotkeysFromConfig(_configStore.appConfig.trayApp?.faderBindings ?? [], _configStore.appConfig.trayApp?.bindings ?? []);
		_ = runStartupHealthAsync();
	}

	async Task runStartupHealthAsync() {
		bool ok = await _mixer.TestConnectionAsync().ConfigureAwait(false);
		ui(() => applyTrayIconState(ok ? AppTrayIconState.OK : AppTrayIconState.NETWORK_ERROR, showErrorOsdIfNotOk: !ok));
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
		if (_osd.IsDisposed) return;
		void Run() {
			if (_osd.IsDisposed) return;
			action();
		}
		if (_osd.IsHandleCreated && _osd.InvokeRequired)
			_osd.BeginInvoke(Run);
		else
			Run();
	}

	async Task handleOscHotkeyAsync(BindingAbstract binding, BindingManager.Slot.Kind kind) {
		string display = binding.displayName;
		bool networkFailed = false;

		if (kind == BindingManager.Slot.Kind.TOGGLE) {
			var t = (BindingToggle)binding;
			ui(() => _osd.ShowPending(display));
			bool? current = await _mixer.QueryToggleAsync(t.address).ConfigureAwait(false);
			if (current == null)
				networkFailed = true;
			else {
				bool nowOn = !current.Value;
				await _mixer.SetToggleAsync(t.address, nowOn).ConfigureAwait(false);
				ui(() => _osd.ShowToggle(t.displayName, nowOn));
			}
		} else if(kind == BindingManager.Slot.Kind.UP || kind == BindingManager.Slot.Kind.DOWN) {
			var f = (BindingFader)binding;
			bool volumeUp = kind == BindingManager.Slot.Kind.UP;
			if (!_mixer.HasFreshFaderSample(f.address))
				ui(() => _osd.ShowPending(display, f.step));
			float? newLevel = await _mixer.NudgeAsync(f.address, volumeUp, f.step, f.minimum, f.maximum).ConfigureAwait(false);
			if (newLevel == null)
				networkFailed = true;
			else {
				ui(() => _osd.ShowLevel(display, f.minimum, f.maximum, newLevel.Value, volumeUp, f.step));
			}
		}

		if (networkFailed)
			ui(() => {
				_tray.ApplyState(AppTrayIconState.NETWORK_ERROR);
				_osd.ShowError();
			});
		else
			ui(() => applyTrayIconState(AppTrayIconState.OK));
	}

	void closeApp() {
		_hook.Dispose();
		_tray.hide();
		System.Windows.Forms.Application.Exit();
	}
}
