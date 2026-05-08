using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using WindowsOscVolumeControl.UI.Osd;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.Input;

namespace WindowsOscVolumeControl.UI.Config.ViewModels;

public sealed class ConfigWindowViewModel : ObservableObject, IDataErrorInfo {
	string _oscIpText = "";
	string _oscPortText = "";
	string _queryTimeoutText = "";
	string _valueCacheTtlText = "";

	string _osdHeightText = "";
	string _osdDurationText = "";
	OSDController.Config.OsdScreenAnchor _osdPosition = OSDController.Config.OsdScreenAnchor.BOTTOM_RIGHT;

	string _hotkeyLongPressMsText = "";
	bool _hotkeyOptimizeNonLongPress;
	bool _hotkeySuppressLongPressOnly;
	bool _hotkeyAcceptMacroChordKeyOrder;

	string _configPathText = "";

	UiTextFeedback _statusFeedback = new("", UiTextFeedbackKind.DEFAULT);
	UiTextFeedback _configFeedback = new("", UiTextFeedbackKind.DEFAULT);
	UiTextFeedback _infoFeedback = new("", UiTextFeedbackKind.DEFAULT);
	UiTextFeedback _autostartFeedback = new("", UiTextFeedbackKind.DEFAULT);
	string? _autostartFeedbackPathOrNull;

	string _oscHeaderText = "OSC";
	string _pingMinText = "—";
	string _pingMedianText = "—";
	string _pingMaxText = "—";
	string _pingLossText = "—";
	string _oscMinText = "—";
	string _oscMedianText = "—";
	string _oscMaxText = "—";
	string _oscLossText = "—";
	string _lossUnitText = "/0";
	Brush? _pingMinForeground;
	Brush? _pingMedianForeground;
	Brush? _pingMaxForeground;
	Brush? _pingLossForeground;
	Brush? _oscMinForeground;
	Brush? _oscMedianForeground;
	Brush? _oscMaxForeground;
	Brush? _oscLossForeground;

	bool _isDragInProgress;
	object? _dragItem;
	ItemsControl? _dragOwnerList;
	double _dragPlaceholderHeight;

	public string oscIpText {
		get => _oscIpText;
		set {
			if (!setTextProperty(ref _oscIpText, value))
				return;
			// Keep cross-field validation reactive (even if only one field was edited).
			raisePropertyChanged(nameof(oscPortText));
		}
	}

	public string oscPortText {
		get => _oscPortText;
		set {
			if (!setTextProperty(ref _oscPortText, value))
				return;
			raisePropertyChanged(nameof(oscIpText));
		}
	}
	public string queryTimeoutText { get => _queryTimeoutText; set => setTextProperty(ref _queryTimeoutText, value); }
	public string valueCacheTtlText { get => _valueCacheTtlText; set => setTextProperty(ref _valueCacheTtlText, value); }

	public string osdHeightText { get => _osdHeightText; set => setTextProperty(ref _osdHeightText, value); }
	public string osdDurationText { get => _osdDurationText; set => setTextProperty(ref _osdDurationText, value); }
	public OSDController.Config.OsdScreenAnchor osdPosition { get => _osdPosition; set => setProperty(ref _osdPosition, value); }

	public string hotkeyLongPressMsText { get => _hotkeyLongPressMsText; set => setTextProperty(ref _hotkeyLongPressMsText, value); }
	public bool hotkeyOptimizeNonLongPress { get => _hotkeyOptimizeNonLongPress; set => setProperty(ref _hotkeyOptimizeNonLongPress, value); }
	public bool hotkeySuppressLongPressOnly { get => _hotkeySuppressLongPressOnly; set => setProperty(ref _hotkeySuppressLongPressOnly, value); }
	public bool hotkeyAcceptMacroChordKeyOrder { get => _hotkeyAcceptMacroChordKeyOrder; set => setProperty(ref _hotkeyAcceptMacroChordKeyOrder, value); }

	public string configPathText { get => _configPathText; set => setTextProperty(ref _configPathText, value); }

	public UiTextFeedback statusFeedback { get => _statusFeedback; set => setProperty(ref _statusFeedback, value); }
	public UiTextFeedback configFeedback { get => _configFeedback; set => setProperty(ref _configFeedback, value); }
	public UiTextFeedback infoFeedback { get => _infoFeedback; set => setProperty(ref _infoFeedback, value); }
	public UiTextFeedback autostartFeedback { get => _autostartFeedback; set => setProperty(ref _autostartFeedback, value); }
	public string? autostartFeedbackPathOrNull { get => _autostartFeedbackPathOrNull; set => setProperty(ref _autostartFeedbackPathOrNull, value); }

	public string oscHeaderText { get => _oscHeaderText; set => setTextProperty(ref _oscHeaderText, value); }
	public string pingMinText { get => _pingMinText; set => setTextProperty(ref _pingMinText, value); }
	public string pingMedianText { get => _pingMedianText; set => setTextProperty(ref _pingMedianText, value); }
	public string pingMaxText { get => _pingMaxText; set => setTextProperty(ref _pingMaxText, value); }
	public string pingLossText { get => _pingLossText; set => setTextProperty(ref _pingLossText, value); }
	public string oscMinText { get => _oscMinText; set => setTextProperty(ref _oscMinText, value); }
	public string oscMedianText { get => _oscMedianText; set => setTextProperty(ref _oscMedianText, value); }
	public string oscMaxText { get => _oscMaxText; set => setTextProperty(ref _oscMaxText, value); }
	public string oscLossText { get => _oscLossText; set => setTextProperty(ref _oscLossText, value); }
	public string lossUnitText { get => _lossUnitText; set => setTextProperty(ref _lossUnitText, value); }

	public Brush? pingMinForeground { get => _pingMinForeground; set => setProperty(ref _pingMinForeground, value); }
	public Brush? pingMedianForeground { get => _pingMedianForeground; set => setProperty(ref _pingMedianForeground, value); }
	public Brush? pingMaxForeground { get => _pingMaxForeground; set => setProperty(ref _pingMaxForeground, value); }
	public Brush? pingLossForeground { get => _pingLossForeground; set => setProperty(ref _pingLossForeground, value); }
	public Brush? oscMinForeground { get => _oscMinForeground; set => setProperty(ref _oscMinForeground, value); }
	public Brush? oscMedianForeground { get => _oscMedianForeground; set => setProperty(ref _oscMedianForeground, value); }
	public Brush? oscMaxForeground { get => _oscMaxForeground; set => setProperty(ref _oscMaxForeground, value); }
	public Brush? oscLossForeground { get => _oscLossForeground; set => setProperty(ref _oscLossForeground, value); }

	public bool isDragInProgress { get => _isDragInProgress; set => setProperty(ref _isDragInProgress, value); }
	public object? dragItem { get => _dragItem; set => setProperty(ref _dragItem, value); }
	public ItemsControl? dragOwnerList { get => _dragOwnerList; set => setProperty(ref _dragOwnerList, value); }
	public double dragPlaceholderHeight { get => _dragPlaceholderHeight; set => setProperty(ref _dragPlaceholderHeight, value); }

	// Binding editor collection (kept as-is; BindingEditor / ControlActionEditor own per-row state).
	public ObservableCollection<BindingEditor> bindings { get; } = [];

	public string Error => "";

	public string this[string columnName] {
		get {
			// Keep this aligned with SettingsFormDraft.tryBuild parsing rules.
			switch (columnName) {
				case nameof(oscIpText):
					return OscConnectionConfigParse.isIpFieldSyntaxOk(oscIpText) ? "" : "Invalid IP address.";
				case nameof(oscPortText):
					return OscConnectionConfigParse.isPortFieldSyntaxOk(oscPortText) ? "" : "Port must be between 1 and 65535.";

				case nameof(queryTimeoutText): {
					return tryValidateUInt(queryTimeoutText, MixerController.Config.MIN_TIMEOUT_MS, MixerController.Config.MAX_TIMEOUT_MS, "Query timeout");
				}
				case nameof(valueCacheTtlText): {
					return tryValidateUInt(valueCacheTtlText, 0, MixerController.Config.MAX_VALUE_CACHE_TTL_MS, "Value cache TTL");
				}
				case nameof(osdHeightText): {
					return tryValidateInt(osdHeightText, OSDController.Config.MIN_HEIGHT_DIP, OSDController.Config.MAX_HEIGHT_DIP, "OSD height");
				}
				case nameof(osdDurationText): {
					return tryValidateUInt(osdDurationText, OSDController.Config.MIN_DISPLAY_DURATION_MS, OSDController.Config.MAX_DISPLAY_DURATION_MS, "OSD display duration");
				}
				case nameof(hotkeyLongPressMsText): {
					return tryValidateUInt(hotkeyLongPressMsText, KeyboardHook.Config.MIN_LONG_PRESS_MS, KeyboardHook.Config.MAX_LONG_PRESS_MS, "Long-press duration");
				}
			}
			return "";
		}
	}

	bool setTextProperty(ref string field, string? value, [CallerMemberName] string? propertyName = null) =>
		setProperty(ref field, value ?? "", propertyName);

	static string tryValidateUInt(string? text, uint min, uint max, string label) {
		if (!uint.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
			return label + " must be an integer.";
		if (parsed < min || parsed > max)
			return label + " must be between " + min + " and " + max + ".";
		return "";
	}

	static string tryValidateInt(string? text, int min, int max, string label) {
		if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
			return label + " must be an integer.";
		if (parsed < min || parsed > max)
			return label + " must be between " + min + " and " + max + ".";
		return "";
	}

	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;

	public ConfigWindowViewModel(AppCoordinator appCoordinator, ConfigStore configStore) {
		_appCoordinator = appCoordinator;
		_configStore = configStore;

		addBindingCommand = new RelayCommand(addBinding);
		openConfigFolderCommand = new RelayCommand(openConfigFolder);
		registerAutostartCommand = new RelayCommand(registerAutostart);
		deregisterAutostartCommand = new RelayCommand(deregisterAutostart);
		deregisterAllAutostartCommand = new RelayCommand(deregisterAllAutostart);

		softDeleteBindingCommand = new RelayCommand<BindingEditor>(softDeleteBinding);
		restoreBindingCommand = new RelayCommand<BindingEditor>(restoreBinding);
		addHotkeyToBindingCommand = new RelayCommand<BindingEditor>(addHotkeyToBinding);
		fillFromX32CatalogCommand = new RelayCommand<BindingEditor>(fillFromX32Catalog);

		softDeleteHotkeyCommand = new RelayCommand<ControlActionEditor>(softDeleteHotkey);
		restoreHotkeyCommand = new RelayCommand<ControlActionEditor>(restoreHotkey);

		loadFromConfigStore();
	}

	public void setConfiguredHotkeysEnabled(bool enabled) => _appCoordinator.setConfiguredHotkeysEnabled(enabled);

	public void loadFromConfigStore() {
		AppConfig cfg = _configStore.appConfig;

		oscIpText = cfg.oscTransport.endPoint.Address.ToString();
		oscPortText = cfg.oscTransport.endPoint.Port.ToString(CultureInfo.InvariantCulture);
		queryTimeoutText = cfg.mixer.timeoutMs.ToString(CultureInfo.InvariantCulture);
		valueCacheTtlText = cfg.mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture);

		osdHeightText = cfg.osd.heightDip.ToString(CultureInfo.InvariantCulture);
		osdDurationText = cfg.osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture);
		osdPosition = cfg.osd.screenAnchor;

		KeyboardHook.Config hk = cfg.keyboardHook;
		hotkeyLongPressMsText = hk.longPressDurationMs.ToString(CultureInfo.InvariantCulture);
		hotkeyOptimizeNonLongPress = hk.optimizeNonLongPressKeyDown;
		hotkeySuppressLongPressOnly = hk.suppressKeyForLongPressOnlyGestures;
		hotkeyAcceptMacroChordKeyOrder = hk.acceptMacroChordKeyOrder;

		configPathText = _configStore.configPathForUi;
		configFeedback = _configStore.lastDiskUiFeedback;
		infoFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);

		WindowsAutostart.UiFeedbackDetail autostart = WindowsAutostart.getCurrentUiFeedback();
		autostartFeedback = autostart.feedback;
		autostartFeedbackPathOrNull = autostart.pathOrNull;

		bindings.Clear();
		foreach (BindingAbstract binding in cfg.trayApp.bindings)
			bindings.Add(BindingEditor.fromBinding(binding));
	}

	public IRelayCommand addBindingCommand { get; }
	public IRelayCommand openConfigFolderCommand { get; }
	public IRelayCommand registerAutostartCommand { get; }
	public IRelayCommand deregisterAutostartCommand { get; }
	public IRelayCommand deregisterAllAutostartCommand { get; }

	public IRelayCommand<BindingEditor> softDeleteBindingCommand { get; }
	public IRelayCommand<BindingEditor> restoreBindingCommand { get; }
	public IRelayCommand<BindingEditor> addHotkeyToBindingCommand { get; }
	public IRelayCommand<BindingEditor> fillFromX32CatalogCommand { get; }

	public IRelayCommand<ControlActionEditor> softDeleteHotkeyCommand { get; }
	public IRelayCommand<ControlActionEditor> restoreHotkeyCommand { get; }

	void addBinding() {
		var ed = new BindingEditor {
			type = BindingEditorType.LINEAR,
			name = "",
			address = "",
			minimum = "0",
			maximum = "1",
			bindingExpanded = true,
		};
		ed.actions.Add(ed.createActionEditor());
		bindings.Add(ed);
	}

	void softDeleteBinding(BindingEditor? item) {
		if (item == null)
			return;
		item.isDeleted = true;
	}

	void restoreBinding(BindingEditor? item) {
		if (item == null)
			return;
		item.isDeleted = false;
	}

	void addHotkeyToBinding(BindingEditor? owner) {
		if (owner == null)
			return;
		owner.actions.Add(owner.createActionEditor());
	}

	void fillFromX32Catalog(BindingEditor? ed) {
		if (ed == null)
			return;
		X32Catalog.ensureLoaded();
		if (X32Catalog.tryResolve(ed.address.Trim(), out X32CatalogEntry e))
			ed.tryApplyX32CatalogEntry(e);
	}

	void softDeleteHotkey(ControlActionEditor? item) {
		if (item == null)
			return;
		item.isDeleted = true;
	}

	void restoreHotkey(ControlActionEditor? item) {
		if (item == null)
			return;
		item.isDeleted = false;
	}

	void openConfigFolder() {
		string? dir = Path.GetDirectoryName(_configStore.configPath);
		if (string.IsNullOrEmpty(dir))
			return;
		try {
			Process.Start(new ProcessStartInfo {
				FileName = "explorer.exe",
				Arguments = "\"" + dir + "\"",
				UseShellExecute = true,
			});
		} catch (Exception ex) {
			statusFeedback = ConfigStore.explorerLaunchFailedFeedback(ex);
		}
	}

	void registerAutostart() {
		autostartFeedback = WindowsAutostart.tryRegister();
		autostartFeedbackPathOrNull = null;
	}

	void deregisterAutostart() {
		autostartFeedback = WindowsAutostart.tryDeregister();
		autostartFeedbackPathOrNull = null;
	}

	void deregisterAllAutostart() {
		UiTextFeedback fb = WindowsAutostart.uiFeedbackForDeregisterAll(WindowsAutostart.tryDeregisterAllCopiesFromRun());
		autostartFeedback = fb;
		autostartFeedbackPathOrNull = null;
	}
}

