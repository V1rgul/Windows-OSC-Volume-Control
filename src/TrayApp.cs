using System.Collections.Generic;
using System.Net;
using System.Windows.Forms;
using WindowsOscVolumeControl;


public class TrayApp : ApplicationContext
{
	/// <summary>OSC fader and toggle bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		/// <summary>Default out-of-box fader row (cosmetic name only; not resolved by code).</summary>
		public static OscBindingFader createDefaultFaderBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/fader",
			step = 0.02f,
			minimum = 0f,
			maximum = 1f,
			hotkeyMinus = Keys.VolumeDown,
			hotkeyPlus = Keys.VolumeUp,
		};

		public static OscBindingToggle createDefaultToggleBinding() => new() {
			name = "MAIN",
			address = "/main/st/mix/on",
			hotkey = Keys.VolumeMute,
		};

		public List<OscBindingFader> faderBindings { get; set; } = [createDefaultFaderBinding()];
		public List<OscBindingToggle> bindings { get; set; } = [createDefaultToggleBinding()];
	}

	readonly ConfigStore _configStore = new();
	readonly ResourceLoader _resources = new();
	private TrayController _tray;

	internal ResourceLoader Resources => _resources;
	private KeyboardHook _hook;
	private OSDController _osd;
	private MixerController _mixer;
	private ConfigForm? _configForm;
	readonly OscBindingManager _oscBindings = new();

	internal ConfigStore configStore => _configStore;

	internal void ResetFaderVolumeCache() => _mixer.ClearFaderSampleCache();

	internal IReadOnlyList<OscBindingToggle> oscToggleBindings => _oscBindings.oscToggleBindings;

	internal IReadOnlyList<OscBindingFader> OscFaderBindings => _oscBindings.OscFaderBindings;

	void rebuildHotkeysFromConfig(IEnumerable<OscBindingFader> faders, IEnumerable<OscBindingToggle> toggles) {
		HashSet<Keys> allKeys = _oscBindings.rebuildFromConfig(faders, toggles);
		_hook.SetConfiguredHotkeys(allKeys);
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

	public TrayApp() {
		_configStore.loadFromDisk();

		_mixer = new MixerController(new OscController(_configStore.appConfig.oscController), _configStore.appConfig.mixer);

		_osd = new OSDController(_configStore.appConfig.osd);
		// Reading Control.Handle forces WinForms to create the native window (HWND) for this form on the current thread. Until that exists, InvokeRequired is not meaningful and ui() could run OSD updates on a background thread.
		_ = _osd.Handle;

		_tray = new TrayController(_resources, openConfig, Exit);

		_hook = new KeyboardHook();
		_hook.OnConfiguredHotkeyPressed = hotkey => _ = OnConfiguredHotkeyAsync(hotkey);

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

		_configForm = new ConfigForm(_mixer, _tray, this, _configStore);
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

	async Task OnConfiguredHotkeyAsync(Keys hotkey) {
		hotkey = KeysUtil.normalize(hotkey);
		_oscBindings.tryGetForHotkey(hotkey, out OscBindingFader? fPlus, out OscBindingFader? fMinus, out OscBindingToggle? toggle);
		if (fPlus != null) {
			await NudgeFaderAsync(fPlus, true).ConfigureAwait(false);
			return;
		}
		if (fMinus != null) {
			await NudgeFaderAsync(fMinus, false).ConfigureAwait(false);
			return;
		}
		if (toggle != null) {
			await FlipOscToggleAsync(toggle).ConfigureAwait(false);
		}
	}

	async Task NudgeFaderAsync(OscBindingFader binding, bool volumeUp) {
		string display = binding.displayName;
		if (!_mixer.HasFreshFaderSample(binding.address))
			ui(() => _osd.ShowPending(display, binding.step));

		float? newLevel = await _mixer.NudgeAsync(binding.address, volumeUp, binding.step, binding.minimum, binding.maximum).ConfigureAwait(false);
		if (newLevel == null) {
			ui(() => {
				_tray.ApplyState(AppTrayIconState.NETWORK_ERROR);
				_osd.ShowError();
			});
			return;
		}

		ui(() => applyTrayIconState(AppTrayIconState.OK));
		ui(() => _osd.ShowLevel(display, binding.minimum, binding.maximum, newLevel.Value, volumeUp, binding.step));
	}

	async Task FlipOscToggleAsync(OscBindingToggle binding) {
		string display = binding.displayName;
		ui(() => _osd.ShowPending(display));
		bool? current = await _mixer.QueryToggleAsync(binding.address).ConfigureAwait(false);
		if (current == null) {
			ui(() => {
				_tray.ApplyState(AppTrayIconState.NETWORK_ERROR);
				_osd.ShowError();
			});
			return;
		}

		bool nowOn = !current.Value;
		await _mixer.SetToggleAsync(binding.address, nowOn).ConfigureAwait(false);
		ui(() => applyTrayIconState(AppTrayIconState.OK));
		ui(() => _osd.ShowToggle(binding.displayName, nowOn));
	}

	void Exit() {
		_hook.Dispose();
		_tray.hide();
		Application.Exit();
	}
}
