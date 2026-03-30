using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using WindowsOscVolumeControl;


public class TrayApp : ApplicationContext
{
	/// <summary>OSC fader and toggle bindings; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public List<OscFaderBinding> faderBindings { get; set; } = [OscFaderBinding.createDefaultMaster()];
		public List<OscToggleBinding> bindings { get; set; } = [OscToggleBinding.createDefaultMasterMute()];
	}

	readonly ConfigStore _configStore = new();
	private NotifyIcon _trayIcon;
	readonly ResourceLoader _resources = new();
	private AppIconController _icons;

	internal ResourceLoader Resources => _resources;
	private KeyboardHook _hook;
	private OSDController _osd;
	private MixerController _mixer;
	private ConfigForm? _configForm;
	readonly object _hotkeySync = new();
	List<OscFaderBinding> _faderBindings = [];
	Dictionary<Keys, OscFaderBinding> _faderMinusByHotkey = [];
	Dictionary<Keys, OscFaderBinding> _faderPlusByHotkey = [];
	List<OscToggleBinding> _oscToggleBindings = [];
	Dictionary<Keys, OscToggleBinding> _oscTogglesByHotkey = [];

	internal ConfigStore configStore => _configStore;

	internal void ResetFaderVolumeCache() => _mixer.ClearFaderSampleCache();

	internal IReadOnlyList<OscToggleBinding> OscToggleBindings {
		get {
			lock (_hotkeySync)
				return _oscToggleBindings.Select(b => new OscToggleBinding(b)).ToArray();
		}
	}

	internal IReadOnlyList<OscFaderBinding> OscFaderBindings {
		get {
			lock (_hotkeySync)
				return _faderBindings.Select(f => new OscFaderBinding(f)).ToArray();
		}
	}

	void RebuildHotkeysFromConfig(IEnumerable<OscFaderBinding> faders, IEnumerable<OscToggleBinding> toggles) {
		List<OscFaderBinding> fd = faders.Select(f => new OscFaderBinding(f)).ToList();
		List<OscToggleBinding> tg = toggles.Select(t => new OscToggleBinding(t) { hotkey = OscHotkey.normalize(t.hotkey) }).ToList();
		var minus = new Dictionary<Keys, OscFaderBinding>();
		var plus = new Dictionary<Keys, OscFaderBinding>();
		var toggleMap = new Dictionary<Keys, OscToggleBinding>();
		var allKeys = new HashSet<Keys>();
		foreach (OscFaderBinding f in fd) {
			if (f.hotkeyMinus != Keys.None) {
				Keys k = OscHotkey.normalize(f.hotkeyMinus);
				minus[k] = f;
				allKeys.Add(k);
			}
			if (f.hotkeyPlus != Keys.None) {
				Keys k = OscHotkey.normalize(f.hotkeyPlus);
				plus[k] = f;
				allKeys.Add(k);
			}
		}
		foreach (OscToggleBinding t in tg) {
			if (t.hotkey == Keys.None)
				continue;
			Keys k = OscHotkey.normalize(t.hotkey);
			toggleMap[k] = t;
			allKeys.Add(k);
		}
		lock (_hotkeySync) {
			_faderBindings = fd;
			_faderMinusByHotkey = minus;
			_faderPlusByHotkey = plus;
			_oscToggleBindings = tg;
			_oscTogglesByHotkey = toggleMap;
		}
		_hook.SetConfiguredHotkeys(allKeys);
	}

	internal void SetOscToggleHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

	internal Icon ApplyTrayIconState(AppTrayIconState state, bool showErrorOsdIfNotOk = false) {
		_icons.ApplyState(state);
		if (showErrorOsdIfNotOk && state != AppTrayIconState.OK)
			_osd.ShowError();
		return _icons.TrayIconSnapshot;
	}

	public void applyConfigFromStore() {
		AppConfig cfg = _configStore.appConfig;
		_mixer.ClearFaderSampleCache();
		_mixer.Osc.ApplyConfig(cfg.oscController);
		_mixer.ApplyConfig(cfg.mixer);
		_osd.ApplyConfig(cfg.osd);
		RebuildHotkeysFromConfig(cfg.trayApp?.faderBindings ?? [], cfg.trayApp?.bindings ?? []);
	}

	public void commitConfigFromSettingsForm(AppConfig newConfig) {
		ArgumentNullException.ThrowIfNull(newConfig);
		_configStore.adoptAppConfig(newConfig);
		applyConfigFromStore();
		_configStore.tryPersistToDisk();
	}

	public TrayApp() {
		_configStore.loadFromDisk();
		_mixer = new MixerController(
			new OscController(_configStore.appConfig.oscController),
			_configStore.appConfig.mixer);

		_osd = new OSDController(_configStore.appConfig.osd);
		_ = _osd.Handle;

		_trayIcon = new NotifyIcon() {
			ContextMenuStrip = new ContextMenuStrip(),
			Visible = true,
			Text = "Windows OSC Volume Control"
		};
		_icons = new AppIconController(_trayIcon, _resources);
		_icons.ApplyState(AppTrayIconState.STARTING_OR_INVALID_CONFIG);
		_trayIcon.ContextMenuStrip.Items.Add("Configure…", null, (s, e) => OpenConfig());
		_trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => Exit());


		_hook = new KeyboardHook();
		_hook.OnConfiguredHotkeyPressed = hotkey => _ = OnConfiguredHotkeyAsync(hotkey);

		RebuildHotkeysFromConfig(_configStore.appConfig.trayApp?.faderBindings ?? [], _configStore.appConfig.trayApp?.bindings ?? []);
		_ = RunStartupHealthAsync();
	}

	async Task RunStartupHealthAsync() {
		bool ok = await _mixer.TestConnectionAsync().ConfigureAwait(false);
		Ui(() => ApplyTrayIconState(ok ? AppTrayIconState.OK : AppTrayIconState.NETWORK_ERROR, showErrorOsdIfNotOk: !ok));
	}

	void OpenConfig() {
		if (_configForm != null && !_configForm.IsDisposed) {
			if (_configForm.WindowState == FormWindowState.Minimized)
				_configForm.WindowState = FormWindowState.Normal;
			_configForm.Activate();
			return;
		}

		_configForm = new ConfigForm(_mixer, _icons, this, _configStore);
		_configForm.FormClosed += (_, _) => _configForm = null;
		_configForm.Show();
	}

	void Ui(Action action) {
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

	static string BindingDisplayName(OscFaderBinding b) {
		if (!string.IsNullOrWhiteSpace(b.name))
			return b.name.Trim();
		if (!string.IsNullOrWhiteSpace(b.address))
			return b.address.Trim();
		return "Fader";
	}

	async Task OnConfiguredHotkeyAsync(Keys hotkey) {
		hotkey = OscHotkey.normalize(hotkey);
		OscFaderBinding? fPlus;
		OscFaderBinding? fMinus;
		OscToggleBinding? toggle;
		lock (_hotkeySync) {
			_faderPlusByHotkey.TryGetValue(hotkey, out fPlus);
			_faderMinusByHotkey.TryGetValue(hotkey, out fMinus);
			_oscTogglesByHotkey.TryGetValue(hotkey, out toggle);
		}
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

	async Task NudgeFaderAsync(OscFaderBinding binding, bool volumeUp) {
		string path = OscController.NormalizeBindingAddress(binding.address);
		string display = BindingDisplayName(binding);
		if (!_mixer.HasFreshFaderSample(path))
			Ui(() => _osd.ShowPending(display, binding.step));

		float? newLevel = await _mixer.NudgeAsync(path, volumeUp, binding.step, binding.minimum, binding.maximum).ConfigureAwait(false);
		if (newLevel == null) {
			Ui(() => {
				_icons.ApplyState(AppTrayIconState.NETWORK_ERROR);
				_osd.ShowError();
			});
			return;
		}

		Ui(() => ApplyTrayIconState(AppTrayIconState.OK));
		Ui(() => _osd.ShowLevel(display, binding.minimum, binding.maximum, newLevel.Value, volumeUp, binding.step));
	}

	async Task FlipOscToggleAsync(OscToggleBinding binding) {
		string display = string.IsNullOrWhiteSpace(binding.name) ? binding.address.Trim() : binding.name.Trim();
		Ui(() => _osd.ShowPending(display));
		bool? current = await _mixer.QueryToggleAsync(binding.address).ConfigureAwait(false);
		if (current == null) {
			Ui(() => {
				_icons.ApplyState(AppTrayIconState.NETWORK_ERROR);
				_osd.ShowError();
			});
			return;
		}

		bool nowOn = !current.Value;
		await _mixer.SetToggleAsync(binding.address, nowOn).ConfigureAwait(false);
		Ui(() => ApplyTrayIconState(AppTrayIconState.OK));
		Ui(() => _osd.ShowToggle(binding.name, nowOn));
	}

	void Exit() {
		_hook.Dispose();
		_trayIcon.Visible = false;
		Application.Exit();
	}
}
