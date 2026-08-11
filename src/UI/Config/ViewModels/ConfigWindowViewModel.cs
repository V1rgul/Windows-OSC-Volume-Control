using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using Result;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.Input;
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
	internal const string FOOTER_ERROR_SEPARATOR = "; ";
	internal const string FOOTER_FIELD_SEPARATOR = "\n";

	OSDController.Config.OsdScreenAnchor _osdPosition = OSDController.Config.OsdScreenAnchor.BOTTOM_RIGHT;

	bool _hotkeyOptimizeNonLongPress;
	bool _hotkeySuppressLongPressOnly;
	bool _hotkeyAcceptMacroChordKeyOrder;

	bool _applyInProgress;

	readonly ResultsRegistry _results = new();

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
		get => _results.text(nameof(oscIpText));
		set => setScalarText(nameof(oscIpText), value);
	}

	public string oscPortText {
		get => _results.text(nameof(oscPortText));
		set => setScalarText(nameof(oscPortText), value);
	}

	public string queryTimeoutText {
		get => _results.text(nameof(queryTimeoutText));
		set => setScalarText(nameof(queryTimeoutText), value);
	}

	public string valueCacheTtlText {
		get => _results.text(nameof(valueCacheTtlText));
		set => setScalarText(nameof(valueCacheTtlText), value);
	}

	public string osdHeightText {
		get => _results.text(nameof(osdHeightText));
		set => setScalarText(nameof(osdHeightText), value);
	}

	public string osdDurationText {
		get => _results.text(nameof(osdDurationText));
		set => setScalarText(nameof(osdDurationText), value);
	}

	public OSDController.Config.OsdScreenAnchor osdPosition { get => _osdPosition; set => setProperty(ref _osdPosition, value); }

	public string hotkeyLongPressMsText {
		get => _results.text(nameof(hotkeyLongPressMsText));
		set => setScalarText(nameof(hotkeyLongPressMsText), value);
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

	public ObservableCollection<BindingEditor> bindings { get; } = [];

	public uint appliedHotkeyLongPressDurationMs => _configStore.appConfig.keyboardHook.longPressDurationMs;
	public bool hasScalarErrors => _results.anyError();
	public bool applyInProgress { get => _applyInProgress; set => setProperty(ref _applyInProgress, value); }

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	public bool HasErrors => hasScalarErrors;

	public System.Collections.IEnumerable GetErrors(string? propertyName) {
		if (string.IsNullOrEmpty(propertyName)) {
			var all = new List<string>();
			foreach (string name in _results.propertyNames)
				all.AddRange(errorMessagesForProperty(name));
			return all.ToArray();
		}
		return errorMessagesForProperty(propertyName).ToArray();
	}

	public bool tryBuildAppConfig(out AppConfig config, out UiTextFeedback? error) {
		if (hasScalarErrors) {
			config = null!;
			error = null;
			return false;
		}

		string bindingErrorsForFooter = formatBindingErrorsForFooter();
		if (!string.IsNullOrEmpty(bindingErrorsForFooter)) {
			config = null!;
			error = new UiTextFeedback(bindingErrorsForFooter, UiTextFeedbackKind.ERROR);
			return false;
		}

		var built = new List<BindingAbstract>();
		for (int i = 0; i < bindings.Count; i++) {
			BindingEditor editor = bindings[i];
			if (editor.isDeleted || editor.isBlank)
				continue;
			if (!editor.tryBuildMaterialized(out BindingAbstract? binding, out string? bindErr)) {
				config = null!;
				error = new UiTextFeedback($"Binding {i + 1}: {bindErr}", UiTextFeedbackKind.ERROR);
				return false;
			}
			built.Add(binding);
		}

		config = new AppConfig();
		_results.put(config);
		config.osd.screenAnchor = OSDController.Config.clampScreenAnchor(osdPosition);
		config.keyboardHook = KeyboardHook.Config.Clamped(new KeyboardHook.Config {
			longPressDurationMs = config.keyboardHook.longPressDurationMs,
			optimizeNonLongPressKeyDown = hotkeyOptimizeNonLongPress,
			suppressKeyForLongPressOnlyGestures = hotkeySuppressLongPressOnly,
			acceptMacroChordKeyOrder = hotkeyAcceptMacroChordKeyOrder,
		});
		config.trayApp.bindings = built;
		error = null;
		return true;
	}

	public string formatBindingErrorsForFooter() {
		var lines = new List<string>();
		for (int i = 0; i < bindings.Count; i++) {
			BindingEditor binding = bindings[i];
			if (binding.isDeleted || binding.isBlank)
				continue;
			foreach (ValidationFieldDescriptor field in binding.footerValidationFields)
				appendBindingFieldErrors(lines, i + 1, binding, field);
			for (int h = 0; h < binding.actions.Count; h++) {
				ControlActionEditor action = binding.actions[h];
				if (action.isDeleted)
					continue;
				foreach (ValidationFieldDescriptor field in action.footerValidationFields)
					appendBindingFieldErrors(lines, i + 1, action, field, hotkeyNumberOneBased: h + 1);
			}
		}
		return string.Join(FOOTER_FIELD_SEPARATOR, lines);
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
	}

	public void setConfiguredHotkeysEnabled(bool enabled) => _appCoordinator.setConfiguredHotkeysEnabled(enabled);

	public void loadFromConfigStore() {
		AppConfig cfg = _configStore.appConfig;

		_results.take(cfg);
		foreach (string propertyName in _results.propertyNames)
			raisePropertyChanged(propertyName);

		osdPosition = cfg.osd.screenAnchor;

		KeyboardHook.Config hk = cfg.keyboardHook;
		hotkeyOptimizeNonLongPress = hk.optimizeNonLongPressKeyDown;
		hotkeySuppressLongPressOnly = hk.suppressKeyForLongPressOnlyGestures;
		hotkeyAcceptMacroChordKeyOrder = hk.acceptMacroChordKeyOrder;

		notifyScalarValidationChanged();

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

	void notifyScalarValidationChanged() {
		foreach (string propertyName in _results.propertyNames)
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
		raisePropertyChanged(nameof(hasScalarErrors));
	}

	void setScalarText(string propertyName, string? value) {
		string text = value ?? "";
		if (_results.text(propertyName) == text)
			return;
		_results.parse(propertyName, text);
		onScalarFieldParsed(propertyName);
	}

	void onScalarFieldParsed(string propertyName) {
		ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
		raisePropertyChanged(nameof(hasScalarErrors));
	}

	static void appendBindingFieldErrors(
		List<string> lines,
		int bindingNumberOneBased,
		INotifyDataErrorInfo source,
		ValidationFieldDescriptor field,
		int? hotkeyNumberOneBased = null) {
		string[] messages = validationErrorsFor(source, field.propertyName).Cast<string>().ToArray();
		if (messages.Length == 0)
			return;
		string prefix = hotkeyNumberOneBased is int hotkey
			? $"Binding {bindingNumberOneBased}, hotkey {hotkey}, {field.label}"
			: $"Binding {bindingNumberOneBased}, {field.label}";
		lines.Add($"{prefix}: {string.Join(FOOTER_ERROR_SEPARATOR, messages)}");
	}

	static IEnumerable<string> validationErrorsFor(INotifyDataErrorInfo source, string propertyName) =>
		source.GetErrors(propertyName).Cast<string>();

	IEnumerable<string> errorMessagesForProperty(string propertyName) {
		if (!_results.tryGet(propertyName, out ResultBridge? bridge))
			return [];
		IResult result = bridge.get();
		if (!result.isError)
			return [];
		return Array.ConvertAll(result.errors, static e => e.ToString());
	}
}
