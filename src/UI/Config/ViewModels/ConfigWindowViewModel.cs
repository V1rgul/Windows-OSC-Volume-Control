using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows.Controls;
using Result;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
using WindowsOscVolumeControl.Osc;
using WindowsOscVolumeControl.UI.Osd;
using AppCoordinator = WindowsOscVolumeControl.App.AppCoordinator;
using Brush = System.Windows.Media.Brush;
using CommunityToolkit.Mvvm.Input;

namespace WindowsOscVolumeControl.UI.Config.ViewModels;

public enum LatencyPanelUiStatus {
	MUTED,
	SUCCESS,
	CAUTION,
	CRITICAL,
}

public sealed class ConfigWindowViewModel : ObservableObject, INotifyDataErrorInfo {
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

	bool _loadingScalars;
	bool _applyInProgress;

	Result<IPAddress> _oscIpResult = OscTransport.Config.parseIpField("");
	Result<int> _oscPortResult = OscTransport.Config.parsePortField("");
	Result<uint> _queryTimeoutResult = MixerController.Config.parseTimeoutMs("");
	Result<uint> _valueCacheTtlResult = MixerController.Config.parseValueCacheTtlMs("");
	Result<int> _osdHeightResult = OSDController.Config.parseHeightDip("");
	Result<uint> _osdDurationResult = OSDController.Config.parseDisplayDurationMs("");
	Result<uint> _hotkeyLongPressMsResult = KeyboardHook.Config.parseLongPressMs("");
	Result<SettingsScalarsMaterialized> _scalarsResult = new ResultError.Generic.Parsing { message = "Invalid IP address." };

	string _configPathText = "";
	string _traceLogPathText = "";

	UiTextFeedback _statusFeedback = new("", UiTextFeedbackKind.DEFAULT);
	UiTextFeedback _diagnosticsFeedback = new("", UiTextFeedbackKind.DEFAULT);
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
			if (!_loadingScalars)
				parseScalarField(nameof(oscIpText));
		}
	}

	public string oscPortText {
		get => _oscPortText;
		set {
			if (!setTextProperty(ref _oscPortText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(oscPortText));
		}
	}

	public string queryTimeoutText {
		get => _queryTimeoutText;
		set {
			if (!setTextProperty(ref _queryTimeoutText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(queryTimeoutText));
		}
	}

	public string valueCacheTtlText {
		get => _valueCacheTtlText;
		set {
			if (!setTextProperty(ref _valueCacheTtlText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(valueCacheTtlText));
		}
	}

	public string osdHeightText {
		get => _osdHeightText;
		set {
			if (!setTextProperty(ref _osdHeightText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(osdHeightText));
		}
	}

	public string osdDurationText {
		get => _osdDurationText;
		set {
			if (!setTextProperty(ref _osdDurationText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(osdDurationText));
		}
	}
	public OSDController.Config.OsdScreenAnchor osdPosition { get => _osdPosition; set => setProperty(ref _osdPosition, value); }

	public string hotkeyLongPressMsText {
		get => _hotkeyLongPressMsText;
		set {
			if (!setTextProperty(ref _hotkeyLongPressMsText, value))
				return;
			if (!_loadingScalars)
				parseScalarField(nameof(hotkeyLongPressMsText));
		}
	}
	public bool hotkeyOptimizeNonLongPress { get => _hotkeyOptimizeNonLongPress; set => setProperty(ref _hotkeyOptimizeNonLongPress, value); }
	public bool hotkeySuppressLongPressOnly { get => _hotkeySuppressLongPressOnly; set => setProperty(ref _hotkeySuppressLongPressOnly, value); }
	public bool hotkeyAcceptMacroChordKeyOrder { get => _hotkeyAcceptMacroChordKeyOrder; set => setProperty(ref _hotkeyAcceptMacroChordKeyOrder, value); }

	public string configPathText { get => _configPathText; set => setTextProperty(ref _configPathText, value); }
	public string traceLogPathText { get => _traceLogPathText; set => setTextProperty(ref _traceLogPathText, value); }

	public UiTextFeedback statusFeedback { get => _statusFeedback; set => setProperty(ref _statusFeedback, value); }
	public UiTextFeedback diagnosticsFeedback { get => _diagnosticsFeedback; set => setProperty(ref _diagnosticsFeedback, value); }
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

	public Result<uint> hotkeyLongPressMsResult => _hotkeyLongPressMsResult;
	public Result<SettingsScalarsMaterialized> scalarsResult => _scalarsResult;
	public bool hasScalarErrors => _scalarsResult.isError;
	public bool applyInProgress { get => _applyInProgress; set => setProperty(ref _applyInProgress, value); }

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	public bool HasErrors => hasScalarErrors;

	public System.Collections.IEnumerable GetErrors(string? propertyName) {
		if (string.IsNullOrEmpty(propertyName))
			return orderedScalarErrorMessages().ToArray();
		return errorMessagesForProperty(propertyName).ToArray();
	}

	public bool tryGetMaterializedScalars(out SettingsScalarsMaterialized scalars, out string? firstError) {
		(bool ok, SettingsScalarsMaterialized materialized, string? error) = _scalarsResult.match(
			v => (true, v, (string?)null),
			errors => (false, default, firstParsingMessage(errors)));
		scalars = materialized;
		firstError = error;
		return ok;
	}

	public string formatScalarErrorsForFooter() {
		var lines = new List<string>();
		foreach (string propertyName in ScalarPropertyNames.all) {
			foreach (global::Result.Result.Error error in scalarResultForProperty(propertyName).errors) {
				string message = parsingMessage(error);
				lines.Add($"{ScalarPropertyNames.humanLabels[propertyName]}: {message}");
			}
		}
		return string.Join(Environment.NewLine, lines);
	}

	public string formatBindingErrorsForFooter() {
		var lines = new List<string>();
		for (int i = 0; i < bindings.Count; i++) {
			BindingEditor binding = bindings[i];
			if (binding.isDeleted || isBindingBlank(binding))
				continue;
			foreach (ValidationFieldDescriptor field in binding.footerValidationFields)
				appendBindingFieldErrors(lines, i + 1, binding, field);
			for (int h = 0; h < binding.actions.Count; h++) {
				ControlActionEditor action = binding.actions[h];
				if (action.isDeleted)
					continue;
				foreach (ValidationFieldDescriptor field in action.footerValidationFields) {
					foreach (string message in validationErrorsFor(action, field.propertyName))
						lines.Add($"Binding {i + 1}, hotkey {h + 1}, {field.label}: {message}");
				}
			}
		}
		return string.Join(Environment.NewLine, lines);
	}

	readonly AppCoordinator _appCoordinator;
	readonly ConfigStore _configStore;

	public ConfigWindowViewModel(AppCoordinator appCoordinator, ConfigStore configStore) {
		_appCoordinator = appCoordinator;
		_configStore = configStore;

		addBindingCommand = new RelayCommand(addBinding);
		openConfigFolderCommand = new RelayCommand(openConfigFolder);
		openTraceLogFolderCommand = new RelayCommand(openTraceLogFolder);
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

		_loadingScalars = true;
		try {
			_oscIpText = cfg.oscTransport.endPoint.Address.ToString();
			raisePropertyChanged(nameof(oscIpText));
			_oscPortText = cfg.oscTransport.endPoint.Port.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(oscPortText));
			_queryTimeoutText = cfg.mixer.timeoutMs.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(queryTimeoutText));
			_valueCacheTtlText = cfg.mixer.ValueCacheTtlMs.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(valueCacheTtlText));

			_osdHeightText = cfg.osd.heightDip.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(osdHeightText));
			_osdDurationText = cfg.osd.DisplayDurationMs.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(osdDurationText));
			osdPosition = cfg.osd.screenAnchor;

			KeyboardHook.Config hk = cfg.keyboardHook;
			_hotkeyLongPressMsText = hk.longPressDurationMs.ToString(CultureInfo.InvariantCulture);
			raisePropertyChanged(nameof(hotkeyLongPressMsText));
			hotkeyOptimizeNonLongPress = hk.optimizeNonLongPressKeyDown;
			hotkeySuppressLongPressOnly = hk.suppressKeyForLongPressOnlyGestures;
			hotkeyAcceptMacroChordKeyOrder = hk.acceptMacroChordKeyOrder;
		} finally {
			_loadingScalars = false;
		}

		foreach (string propertyName in ScalarPropertyNames.all)
			parseScalarField(propertyName, recomputeAggregate: false);
		recomputeScalarsResult();

		configPathText = _configStore.configPathForUi;
		traceLogPathText = AppTrace.traceLogFilePathForUi;
		configFeedback = _configStore.lastDiskUiFeedback;
		infoFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		diagnosticsFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);

		WindowsAutostart.UiFeedbackDetail autostart = WindowsAutostart.getCurrentUiFeedback();
		autostartFeedback = autostart.feedback;
		autostartFeedbackPathOrNull = autostart.pathOrNull;

		bindings.Clear();
		foreach (BindingAbstract binding in cfg.trayApp.bindings)
			bindings.Add(BindingEditor.fromBinding(binding));
	}

	public IRelayCommand addBindingCommand { get; }
	public IRelayCommand openConfigFolderCommand { get; }
	public IRelayCommand openTraceLogFolderCommand { get; }
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
		launchExplorerOnDirectory(Path.GetDirectoryName(_configStore.configPath));
	}

	void openTraceLogFolder() {
		launchExplorerOnDirectory(Path.GetDirectoryName(AppTrace.traceLogFilePathForUi));
	}

	void launchExplorerOnDirectory(string? dir) {
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

	public void applyLatencyStatsToUi(int timeoutMs, RttStatsSnapshot ping, RttStatsSnapshot osc, Func<LatencyPanelUiStatus, Brush> brushForStatus) {
		pingMinText = formatLatencyCellText(ping.minMs);
		pingMedianText = formatLatencyCellText(ping.medianMs);
		pingMaxText = formatLatencyCellText(ping.maxMs);
		pingLossText = ping.completedCount == 0 ? "—" : ping.receivedCount.ToString(CultureInfo.InvariantCulture);

		oscMinText = formatLatencyCellText(osc.minMs);
		oscMedianText = formatLatencyCellText(osc.medianMs);
		oscMaxText = formatLatencyCellText(osc.maxMs);
		oscLossText = osc.completedCount == 0 ? "—" : osc.receivedCount.ToString(CultureInfo.InvariantCulture);

		int completed = ping.completedCount;
		lossUnitText = "/" + completed.ToString(CultureInfo.InvariantCulture);

		pingLossForeground = brushForStatus(responseStatus(ping.completedCount, ping.receivedCount));
		oscLossForeground = brushForStatus(responseStatus(osc.completedCount, osc.receivedCount));

		pingMinForeground = brushForStatus(latencyStatus(timeoutMs, ping.minMs));
		pingMedianForeground = brushForStatus(latencyStatus(timeoutMs, ping.medianMs));
		pingMaxForeground = brushForStatus(latencyStatus(timeoutMs, ping.maxMs));

		oscMinForeground = brushForStatus(latencyStatus(timeoutMs, osc.minMs));
		oscMedianForeground = brushForStatus(latencyStatus(timeoutMs, osc.medianMs));
		oscMaxForeground = brushForStatus(latencyStatus(timeoutMs, osc.maxMs));
	}

	static string formatLatencyCellText(int? rttMs) {
		if (rttMs == null)
			return "—";
		int ms = rttMs.Value;
		if (ms == 0)
			return "<1";
		return ms.ToString(CultureInfo.InvariantCulture);
	}

	static LatencyPanelUiStatus responseStatus(int completed, int received) {
		if (completed <= 0)
			return LatencyPanelUiStatus.MUTED;
		if (received <= 0)
			return LatencyPanelUiStatus.CRITICAL;
		return received < completed ? LatencyPanelUiStatus.CAUTION : LatencyPanelUiStatus.SUCCESS;
	}

	static LatencyPanelUiStatus latencyStatus(int timeoutMs, int? rttMs) {
		if (rttMs == null)
			return LatencyPanelUiStatus.MUTED;
		if (timeoutMs <= 0)
			return LatencyPanelUiStatus.MUTED;
		double ratio = rttMs.Value / (double)timeoutMs;
		if (ratio < 0.10)
			return LatencyPanelUiStatus.SUCCESS;
		if (ratio < 0.50)
			return LatencyPanelUiStatus.CAUTION;
		return LatencyPanelUiStatus.CRITICAL;
	}

	void parseScalarField(string propertyName, bool recomputeAggregate = true) {
		switch (propertyName) {
			case nameof(oscIpText):
				_oscIpResult = OscTransport.Config.parseIpField(_oscIpText);
				break;
			case nameof(oscPortText):
				_oscPortResult = OscTransport.Config.parsePortField(_oscPortText);
				break;
			case nameof(queryTimeoutText):
				_queryTimeoutResult = MixerController.Config.parseTimeoutMs(_queryTimeoutText);
				break;
			case nameof(valueCacheTtlText):
				_valueCacheTtlResult = MixerController.Config.parseValueCacheTtlMs(_valueCacheTtlText);
				break;
			case nameof(osdHeightText):
				_osdHeightResult = OSDController.Config.parseHeightDip(_osdHeightText);
				break;
			case nameof(osdDurationText):
				_osdDurationResult = OSDController.Config.parseDisplayDurationMs(_osdDurationText);
				break;
			case nameof(hotkeyLongPressMsText):
				_hotkeyLongPressMsResult = KeyboardHook.Config.parseLongPressMs(_hotkeyLongPressMsText);
				break;
			default:
				return;
		}

		ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
		if (propertyName == nameof(hotkeyLongPressMsText))
			raisePropertyChanged(nameof(hotkeyLongPressMsResult));

		if (recomputeAggregate && !_loadingScalars)
			recomputeScalarsResult();
	}

	void recomputeScalarsResult() {
		foreach (string propertyName in ScalarPropertyNames.all) {
			if (scalarResultForProperty(propertyName).isError) {
				global::Result.Result.Error[] errors = scalarResultForProperty(propertyName).errors;
				_scalarsResult = errors;
				raisePropertyChanged(nameof(scalarsResult));
				raisePropertyChanged(nameof(hasScalarErrors));
				return;
			}
		}

		_scalarsResult = new SettingsScalarsMaterialized(
			new IPEndPoint(_oscIpResult.value, _oscPortResult.value),
			_queryTimeoutResult.value,
			_valueCacheTtlResult.value,
			_osdHeightResult.value,
			_osdDurationResult.value,
			_hotkeyLongPressMsResult.value);
		raisePropertyChanged(nameof(scalarsResult));
		raisePropertyChanged(nameof(hasScalarErrors));
	}

	IResult scalarResultForProperty(string propertyName) => propertyName switch {
		nameof(oscIpText) => _oscIpResult,
		nameof(oscPortText) => _oscPortResult,
		nameof(queryTimeoutText) => _queryTimeoutResult,
		nameof(valueCacheTtlText) => _valueCacheTtlResult,
		nameof(osdHeightText) => _osdHeightResult,
		nameof(osdDurationText) => _osdDurationResult,
		nameof(hotkeyLongPressMsText) => _hotkeyLongPressMsResult,
		_ => _scalarsResult,
	};

	static void appendBindingFieldErrors(List<string> lines, int bindingNumberOneBased, BindingEditor binding, ValidationFieldDescriptor field) {
		foreach (string message in validationErrorsFor(binding, field.propertyName))
			lines.Add($"Binding {bindingNumberOneBased}, {field.label}: {message}");
	}

	static IEnumerable<string> validationErrorsFor(INotifyDataErrorInfo source, string propertyName) =>
		source.GetErrors(propertyName).Cast<string>();

	static bool isBindingBlank(BindingEditor editor) =>
		string.IsNullOrWhiteSpace(editor.name)
		&& string.IsNullOrWhiteSpace(editor.address)
		&& string.IsNullOrWhiteSpace(editor.minimum)
		&& string.IsNullOrWhiteSpace(editor.maximum)
		&& string.IsNullOrWhiteSpace(editor.rangeMinimum)
		&& string.IsNullOrWhiteSpace(editor.rangeMaximum)
		&& string.IsNullOrWhiteSpace(editor.unit)
		&& editor.actions.Count == 0;

	IEnumerable<string> errorMessagesForProperty(string propertyName) => propertyName switch {
		nameof(oscIpText) => _oscIpResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(oscPortText) => _oscPortResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(queryTimeoutText) => _queryTimeoutResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(valueCacheTtlText) => _valueCacheTtlResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(osdHeightText) => _osdHeightResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(osdDurationText) => _osdDurationResult.match(_ => [], errors => errors.Select(parsingMessage)),
		nameof(hotkeyLongPressMsText) => _hotkeyLongPressMsResult.match(_ => [], errors => errors.Select(parsingMessage)),
		_ => [],
	};

	IEnumerable<string> orderedScalarErrorMessages() {
		var messages = new List<string>();
		foreach (string propertyName in ScalarPropertyNames.all)
			messages.AddRange(errorMessagesForProperty(propertyName));
		return messages;
	}

	static string parsingMessage(global::Result.Result.Error error) =>
		error is ResultErrorWithMsg withMsg ? withMsg.message : "Invalid value.";

	static string? firstParsingMessage(global::Result.Result.Error[] errors) =>
		errors.Length > 0 ? parsingMessage(errors[0]) : null;
}

