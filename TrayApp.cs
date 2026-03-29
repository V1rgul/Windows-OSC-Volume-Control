using System.Collections.Generic;
using System.Net;
using System.Windows.Forms;
using X32VolumeHijacker;


public class TrayApp : ApplicationContext
{
	/// <summary>OSC toggle hotkey bindings DTO; persisted via <see cref="ConfigStore"/>.</summary>
	public sealed class Config {
		public List<OscToggleBinding> Bindings { get; set; } = [];
	}

	readonly ConfigStore _configStore = new();
	private NotifyIcon _trayIcon;
	private AppIconController _icons;
	private KeyboardHook _hook;
	private VolumeOsd _osd;
	private OscController _osc;
	private FaderVolumeAdjuster _faderVolume;
	private ConfigForm? _configForm;
	readonly object _oscToggleSync = new();
	List<OscToggleBinding> _oscToggleBindings = [];
	Dictionary<Keys, OscToggleBinding> _oscTogglesByHotkey = [];

	internal ConfigStore ConfigStore => _configStore;

	internal void ResetFaderVolumeCache() => _faderVolume.ClearFaderSampleCache();

	internal IReadOnlyList<OscToggleBinding> OscToggleBindings {
		get {
			lock (_oscToggleSync)
				return _oscToggleBindings.Select(b => new OscToggleBinding(b)).ToArray();
		}
	}

	internal void SetOscToggleBindings(IEnumerable<OscToggleBinding> bindings) {
		ArgumentNullException.ThrowIfNull(bindings);
		List<OscToggleBinding> ordered = bindings
			.Select(b => new OscToggleBinding(b) { Hotkey = OscHotkey.Normalize(b.Hotkey) })
			.ToList();
		Dictionary<Keys, OscToggleBinding> updated = ordered.ToDictionary(b => b.Hotkey, b => b);
		lock (_oscToggleSync) {
			_oscToggleBindings = ordered;
			_oscTogglesByHotkey = updated;
		}
		_hook.SetConfiguredHotkeys(updated.Keys);
	}

	internal void SetOscToggleHotkeysEnabled(bool enabled) => _hook.SetConfiguredHotkeysEnabled(enabled);

	internal Icon ApplyTrayIconState(AppTrayIconState state, bool showErrorOsdIfNotOk = false) {
		_icons.ApplyState(state);
		if (showErrorOsdIfNotOk && state != AppTrayIconState.Ok)
			_osd.ShowError();
		return _icons.TrayIconSnapshot;
	}

	/// <summary>Applies <see cref="ConfigStore.AppConfig"/> to OSC, fader adjuster, and toggle bindings.</summary>
	public void ApplyConfigFromStore() {
		AppConfig cfg = _configStore.AppConfig;
		_faderVolume.ClearFaderSampleCache();
		_osc.ApplyConfig(new OscController.Config(cfg.Osc));
		_faderVolume.ApplyConfig(cfg.Fader ?? new FaderVolumeAdjuster.Config());
		SetOscToggleBindings(cfg.OscToggles?.Bindings ?? []);
	}

	/// <summary>Adopts config from the settings form, applies it, then attempts to persist (failure does not revert memory).</summary>
	public void CommitConfigFromSettingsForm(AppConfig newConfig) {
		ArgumentNullException.ThrowIfNull(newConfig);
		_configStore.AdoptAppConfig(newConfig);
		ApplyConfigFromStore();
		_configStore.TryPersistToDisk();
	}

	public TrayApp() {
		var bootstrapOsc = AppConfig.CreateDefaults().Osc;
		_osc = new OscController(bootstrapOsc);
		_faderVolume = new FaderVolumeAdjuster(_osc);

		_osd = new VolumeOsd();
		_ = _osd.Handle;

		_trayIcon = new NotifyIcon() {
			ContextMenuStrip = new ContextMenuStrip(),
			Visible = true,
			Text = "X32 Volume Hijacker"
		};
		_icons = new AppIconController(_trayIcon);
		_icons.ApplyState(AppTrayIconState.StartingOrInvalidConfig);
		_trayIcon.ContextMenuStrip.Items.Add("Configure X32", null, (s, e) => OpenConfig());
		_trayIcon.ContextMenuStrip.Items.Add("Exit", null, (s, e) => Exit());


		_hook = new KeyboardHook();
		_hook.OnVolumeKeyPressed = key => _ = AdjustVolume(key);
		_hook.OnConfiguredHotkeyPressed = hotkey => _ = ToggleOscBinding(hotkey);

		_configStore.LoadFromDisk();
		ApplyConfigFromStore();
		_ = RunStartupHealthAsync();
	}

	async Task RunStartupHealthAsync() {
		bool ok = await _osc.TestConnectionAsync().ConfigureAwait(false);
		Ui(() => ApplyTrayIconState(ok ? AppTrayIconState.Ok : AppTrayIconState.NetworkError, showErrorOsdIfNotOk: !ok));
	}

	void OpenConfig() {
		if (_configForm != null && !_configForm.IsDisposed) {
			if (_configForm.WindowState == FormWindowState.Minimized)
				_configForm.WindowState = FormWindowState.Normal;
			_configForm.Activate();
			return;
		}

		_configForm = new ConfigForm(_osc, _icons, this, _configStore);
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

	async Task AdjustVolume(KeyboardHook.VolumeKey key) {
		if (key == KeyboardHook.VolumeKey.MUTE_TOGGLE) {
			await ToggleMute();
			return;
		}

		if (_icons.State != AppTrayIconState.Ok) {
			Ui(() => _osd.ShowError());
			return;
		}

		if (!_faderVolume.HasFreshFaderSample)
			Ui(() => _osd.ShowPending());

		float? newLevel = await _faderVolume.NudgeAsync(key);
		if (newLevel == null) {
			Ui(() => ApplyTrayIconState(AppTrayIconState.NetworkError, showErrorOsdIfNotOk: true));
			return;
		}

		Ui(() => ApplyTrayIconState(AppTrayIconState.Ok));
		Ui(() => _osd.ShowLevel(newLevel.Value, key == KeyboardHook.VolumeKey.UP));
	}

	async Task ToggleMute() {
		if (_icons.State != AppTrayIconState.Ok) {
			Ui(() => _osd.ShowError());
			return;
		}

		Ui(() => _osd.ShowPending());
		bool? muted = await _osc.QueryMuteAsync();
		if (muted == null) {
			Ui(() => ApplyTrayIconState(AppTrayIconState.NetworkError, showErrorOsdIfNotOk: true));
			return;
		}

		Ui(() => ApplyTrayIconState(AppTrayIconState.Ok));
		bool nowMuted = !muted.Value;
		await _osc.SetMuteAsync(nowMuted);
		bool showMuted = nowMuted;
		Ui(() => _osd.ShowMute(showMuted));
	}

	async Task ToggleOscBinding(Keys hotkey) {
		OscToggleBinding? binding;
		hotkey = OscHotkey.Normalize(hotkey);
		lock (_oscToggleSync)
			_oscTogglesByHotkey.TryGetValue(hotkey, out binding);
		if (binding == null)
			return;

		if (_icons.State != AppTrayIconState.Ok) {
			Ui(() => _osd.ShowError());
			return;
		}

		Ui(() => _osd.ShowPending());
		bool? isOn = await _osc.QueryToggleAsync(binding.Address);
		if (isOn == null) {
			Ui(() => ApplyTrayIconState(AppTrayIconState.NetworkError, showErrorOsdIfNotOk: true));
			return;
		}

		bool nowOn = !isOn.Value;
		await _osc.SetToggleAsync(binding.Address, nowOn);
		Ui(() => ApplyTrayIconState(AppTrayIconState.Ok));
		Ui(() => _osd.ShowToggle(binding.Name, nowOn));
	}

	void Exit() {
		_hook.Dispose();
		_trayIcon.Visible = false;
		Application.Exit();
	}
}
