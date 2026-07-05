using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using JetBrains.Annotations;
using Result;
using WindowsOscVolumeControl.Binding;
using WindowsOscVolumeControl.Diagnostics;
using WindowsOscVolumeControl.UI.Osd;

namespace WindowsOscVolumeControl.UI.Config;

public enum BindingEditorType {
	LINEAR,
	TOGGLE,
	LINF,
	LOGF,
	LEVEL,
}

public sealed class BindingTypeUiChoice(BindingEditorType value, string label, string category) {
	[UsedImplicitly]
	public BindingEditorType value { get; } = value;
	[UsedImplicitly]
	public string label { get; } = label;
	public string category { get; } = category;
}

public sealed class OsdAnchorUiChoice(OSDController.Config.OsdScreenAnchor value, string label) {
	[UsedImplicitly]
	public OSDController.Config.OsdScreenAnchor value { get; } = value;
	[UsedImplicitly]
	public string label { get; } = label;
}

public static class OsdAnchorUiChoices {
	public static IReadOnlyList<OsdAnchorUiChoice> All { get; } = [
		new(OSDController.Config.OsdScreenAnchor.TOP_LEFT, "Top left"),
		new(OSDController.Config.OsdScreenAnchor.TOP_CENTER, "Top center"),
		new(OSDController.Config.OsdScreenAnchor.TOP_RIGHT, "Top right"),
		new(OSDController.Config.OsdScreenAnchor.MIDDLE_LEFT, "Middle left"),
		new(OSDController.Config.OsdScreenAnchor.MIDDLE_RIGHT, "Middle right"),
		new(OSDController.Config.OsdScreenAnchor.BOTTOM_LEFT, "Bottom left"),
		new(OSDController.Config.OsdScreenAnchor.BOTTOM_CENTER, "Bottom center"),
		new(OSDController.Config.OsdScreenAnchor.BOTTOM_RIGHT, "Bottom right"),
	];
}

public sealed class ControlActionChoice(Type actionType, string displayName) {
	public Type actionType { get; } = actionType;
	[UsedImplicitly]
	public string displayName { get; } = displayName;
}

public readonly record struct ValidationFieldDescriptor(string propertyName, string label);

public abstract class ObservableObject : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;

	protected bool setProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	protected bool setTextProperty(ref string field, string? value, [CallerMemberName] string? propertyName = null) =>
		setProperty(ref field, value ?? "", propertyName);

	protected void raisePropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public abstract class ObservableValidationObject : ObservableObject, INotifyDataErrorInfo {
	readonly Dictionary<string, string[]> _validationErrors = new(StringComparer.Ordinal);

	public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

	public bool HasErrors => _validationErrors.Count > 0;

	public IEnumerable GetErrors(string? propertyName) {
		if (string.IsNullOrEmpty(propertyName))
			return _validationErrors.Values.SelectMany(static e => e).ToArray();
		return _validationErrors.TryGetValue(propertyName, out string[]? errors)
			? errors
			: Array.Empty<string>();
	}

	protected void setValidationErrors(string propertyName, IEnumerable<string> errors) {
		string[] next = errors.Where(static e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.Ordinal).ToArray();
		bool hadCurrent = _validationErrors.TryGetValue(propertyName, out string[]? current);
		if (hadCurrent && current.SequenceEqual(next, StringComparer.Ordinal))
			return;

		if (next.Length == 0)
			_validationErrors.Remove(propertyName);
		else
			_validationErrors[propertyName] = next;

		ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
		raisePropertyChanged(nameof(HasErrors));
	}

	protected void clearValidationErrors() {
		if (_validationErrors.Count == 0)
			return;
		string[] names = _validationErrors.Keys.ToArray();
		_validationErrors.Clear();
		foreach (string name in names)
			ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(name));
		raisePropertyChanged(nameof(HasErrors));
	}

	protected static string[] validationMessages(IResult result) =>
		result.isError
			? result.errors.Select(parsingMessage).ToArray()
			: [];

	static string parsingMessage(global::Result.Result.Error error) =>
		error is ResultErrorWithMsg withMsg ? withMsg.message : "Invalid value.";
}

public sealed class BindingEditor : ObservableValidationObject {
	static readonly List<BindingTypeUiChoice> _typeChoiceList = [
		new(BindingEditorType.LINEAR, "Linear", ""),
		new(BindingEditorType.TOGGLE, "Toggle", ""),
		new(BindingEditorType.LINF, "linf", "X32 Specific"),
		new(BindingEditorType.LOGF, "logf", "X32 Specific"),
		new(BindingEditorType.LEVEL, "level", "X32 Specific"),
	];

	BindingEditorType _type = BindingEditorType.LINEAR;
	string _name = "";
	string _address = "";
	string? _selectedAddressSuggestion;
	bool _suppressAddressSuggestionRebuild;
	string _minimum = "0";
	string _maximum = "1";
	string _rangeMinimum = "";
	string _rangeMaximum = "";
	string _unit = "";
	bool _hasX32CatalogMatch;
	string _x32CatalogTooltip = "No X32 catalog match for this address.";
	bool _isDeleted;
	bool _bindingExpanded;
	IReadOnlyList<ControlActionChoice>? _actionChoices;

	Result<string> _nameResult = BindingManager.Config.parseBindingNameField("");
	Result<string> _addressResult = BindingManager.Config.parseOscAddressField("");
	Result<BindingManager.Config.FloatFieldValue> _minimumResult = BindingManager.Config.parseContinuousFloatField("0");
	Result<BindingManager.Config.FloatFieldValue> _maximumResult = BindingManager.Config.parseContinuousFloatField("1");
	Result<BindingManager.Config.FloatFieldValue> _rangeMinimumResult = BindingManager.Config.parseContinuousFloatField("");
	Result<BindingManager.Config.FloatFieldValue> _rangeMaximumResult = BindingManager.Config.parseContinuousFloatField("");
	Result<string?> _unitResult = BindingManager.Config.parseUnitField("");

	readonly ObservableCollection<string> _addressSuggestions = [];

	public BindingEditor() {
		// Per-row view instance: avoids WPF "currency" leaking across multiple ComboBoxes.
		var cvs = new CollectionViewSource { Source = _typeChoiceList };
		cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(BindingTypeUiChoice.category)));
		groupedTypeChoices = cvs.View;

		X32Catalog.ensureLoaded();
		rebuildAddressSuggestions();
		refreshX32CatalogMatch();
		recomputeValidation();
	}

	public ICollectionView groupedTypeChoices { get; }

	public string name {
		get => _name;
		set {
			if (setTextProperty(ref _name, value))
				recomputeValidation();
		}
	}

	public string address {
		get => _address;
		set {
			if (setTextProperty(ref _address, value)) {
				// User-typed edits should not keep a stale selection.
				if (_selectedAddressSuggestion != null && !string.Equals(_selectedAddressSuggestion, _address, StringComparison.Ordinal)) {
					_selectedAddressSuggestion = null;
					raisePropertyChanged(nameof(selectedAddressSuggestion));
				}
				if (!_suppressAddressSuggestionRebuild)
					rebuildAddressSuggestions();
				refreshX32CatalogMatch();
				recomputeValidation();
			}
		}
	}

	public ObservableCollection<string> addressSuggestions => _addressSuggestions;

	public string? selectedAddressSuggestion {
		get => _selectedAddressSuggestion;
		set {
			if (!setProperty(ref _selectedAddressSuggestion, value))
				return;
			if (!string.IsNullOrWhiteSpace(value)) {
				try {
					_suppressAddressSuggestionRebuild = true;
					address = value;
				} finally {
					_suppressAddressSuggestionRebuild = false;
				}
			}
		}
	}

	void rebuildAddressSuggestions() {
		_addressSuggestions.Clear();
		string needle = _address.Trim();
		const int limit = 200;
		int count = 0;
		foreach (string s in X32Catalog.addressPatterns) {
			if (needle.Length == 0 || s.Contains(needle, StringComparison.OrdinalIgnoreCase)) {
				_addressSuggestions.Add(s);
				count++;
				if (count >= limit)
					break;
			}
		}
	}

	public bool hasX32CatalogMatch => _hasX32CatalogMatch;

	public string x32CatalogTooltip => _x32CatalogTooltip;

	void refreshX32CatalogMatch() {
		string a = _address.Trim();
		if (a.Length == 0) {
			_hasX32CatalogMatch = false;
			_x32CatalogTooltip = "Enter an OSC address to check the X32 catalog.";
		} else if (X32Catalog.tryResolve(a, out X32CatalogEntry e)) {
			_hasX32CatalogMatch = true;
			string kind = e.kind switch {
				X32CatalogKind.Linf => "linf",
				X32CatalogKind.Logf => "logf",
				X32CatalogKind.Level => "level",
				X32CatalogKind.Toggle => "toggle",
				_ => "x32",
			};
			string catalogUnit = e.unit ?? (e.kind == X32CatalogKind.Level ? "dB" : "");
			_x32CatalogTooltip = string.IsNullOrWhiteSpace(catalogUnit)
				? ("Detected: " + kind)
				: ("Detected: " + kind + " · " + catalogUnit);
		} else {
			_hasX32CatalogMatch = false;
			_x32CatalogTooltip = "No X32 catalog match for this address.";
		}
		raisePropertyChanged(nameof(hasX32CatalogMatch));
		raisePropertyChanged(nameof(x32CatalogTooltip));
	}

	public BindingEditorType type {
		get => _type;
		set {
			if (!setProperty(ref _type, value))
				return;
			if (value is BindingEditorType.LINF or BindingEditorType.LOGF
			    && string.IsNullOrWhiteSpace(_rangeMinimum) && string.IsNullOrWhiteSpace(_rangeMaximum)) {
				_rangeMinimum = _minimum;
				_rangeMaximum = _maximum;
				raisePropertyChanged(nameof(rangeMinimum));
				raisePropertyChanged(nameof(rangeMaximum));
			}
			rebuildActionChoices();
			raisePropertyChanged(nameof(isLinear));
			raisePropertyChanged(nameof(showsMinMax));
			raisePropertyChanged(nameof(showsRange));
			raisePropertyChanged(nameof(showsUnit));
			raisePropertyChanged(nameof(typeDisplayLabel));
			raisePropertyChanged(nameof(actionChoices));
			pruneActionsForType();
			foreach (ControlActionEditor h in actions)
				h.refreshChoiceFromOwner();
			recomputeValidation();
		}
	}

	public string minimum {
		get => _minimum;
		set {
			if (setTextProperty(ref _minimum, value))
				recomputeValidation();
		}
	}

	public string maximum {
		get => _maximum;
		set {
			if (setTextProperty(ref _maximum, value))
				recomputeValidation();
		}
	}

	public string rangeMinimum {
		get => _rangeMinimum;
		set {
			if (setTextProperty(ref _rangeMinimum, value))
				recomputeValidation();
		}
	}

	public string rangeMaximum {
		get => _rangeMaximum;
		set {
			if (setTextProperty(ref _rangeMaximum, value))
				recomputeValidation();
		}
	}

	public string unit {
		get => _unit;
		set {
			if (!setTextProperty(ref _unit, value))
				return;
			foreach (ControlActionEditor h in actions)
				h.raiseFloatValueLabelChanged();
			recomputeValidation();
		}
	}

	public bool isDeleted {
		get => _isDeleted;
		set {
			if (!setProperty(ref _isDeleted, value))
				return;
			raisePropertyChanged(nameof(isNotDeleted));
			bindingExpanded = !value;
			recomputeValidation();
		}
	}

	public bool isNotDeleted => !_isDeleted;

	public bool bindingExpanded {
		get => _bindingExpanded;
		set => setProperty(ref _bindingExpanded, value);
	}

	public ObservableCollection<ControlActionEditor> actions { get; } = [];

	public bool isLinear => type == BindingEditorType.LINEAR;
	public bool showsMinMax => type is BindingEditorType.LINEAR or BindingEditorType.LINF or BindingEditorType.LOGF or BindingEditorType.LEVEL;
	public bool showsRange => type is BindingEditorType.LINF or BindingEditorType.LOGF;
	public bool showsUnit => type is BindingEditorType.LINF or BindingEditorType.LOGF;

	public IEnumerable<ValidationFieldDescriptor> footerValidationFields {
		get {
			yield return new(nameof(name), "Name");
			yield return new(nameof(address), "OSC address");
			if (showsMinMax) {
				yield return new(nameof(minimum), "Minimum");
				yield return new(nameof(maximum), "Maximum");
			}
			if (showsRange) {
				yield return new(nameof(rangeMinimum), "Range min");
				yield return new(nameof(rangeMaximum), "Range max");
			}
			if (showsUnit)
				yield return new(nameof(unit), "Unit");
		}
	}

	public string typeDisplayLabel => type switch {
		BindingEditorType.LINEAR => "Linear",
		BindingEditorType.TOGGLE => "Toggle",
		BindingEditorType.LINF => "linf",
		BindingEditorType.LOGF => "logf",
		BindingEditorType.LEVEL => "level",
		_ => "",
	};

	public IReadOnlyList<ControlAction> availableActionPrototypes =>
		type switch {
			BindingEditorType.LINEAR => BindingEditorStatics.linearPrototypes,
			BindingEditorType.TOGGLE => BindingEditorStatics.togglePrototypes,
			_ => BindingEditorStatics.x32FloatPrototypes,
		};

	public IReadOnlyList<ControlActionChoice> actionChoices {
		get {
			_actionChoices ??= buildActionChoices();
			return _actionChoices;
		}
	}

	void rebuildActionChoices() {
		_actionChoices = buildActionChoices();
	}

	List<ControlActionChoice> buildActionChoices() =>
		availableActionPrototypes.Select(static p => new ControlActionChoice(p.GetType(), p.name)).ToList();

	void pruneActionsForType() {
		for (int i = actions.Count - 1; i >= 0; i--) {
			if (!actions[i].isCompatibleWithBindingType(type))
				actions.RemoveAt(i);
		}
	}

	public bool tryApplyX32CatalogEntry(X32CatalogEntry e) {
		type = e.kind switch {
			X32CatalogKind.Linf => BindingEditorType.LINF,
			X32CatalogKind.Logf => BindingEditorType.LOGF,
			X32CatalogKind.Level => BindingEditorType.LEVEL,
			X32CatalogKind.Toggle => BindingEditorType.TOGGLE,
			_ => BindingEditorType.LINF,
		};
		if (type != BindingEditorType.TOGGLE) {
			int minDig = ContinuousFloatUtil.fractionalDigitsForValue(e.minimum);
			int maxDig = ContinuousFloatUtil.fractionalDigitsForValue(e.maximum);
			minimum = ContinuousFloatUtil.formatFloatForConfig(e.minimum, minDig);
			maximum = ContinuousFloatUtil.formatFloatForConfig(e.maximum, maxDig);
		}
		if (type is BindingEditorType.LINF or BindingEditorType.LOGF) {
			unit = e.unit ?? "";
			rangeMinimum = minimum;
			rangeMaximum = maximum;
		} else {
			unit = "";
		}
		return true;
	}

	public static BindingEditor fromBinding(BindingAbstract binding) {
		var ed = new BindingEditor();
		switch (binding) {
			case BindingLinear f:
				ed.name = f.name;
				ed.address = f.address;
				ed.minimum = ContinuousFloatUtil.formatFloatForConfig(f.minimum, f.minimumFractionalDigits);
				ed.maximum = ContinuousFloatUtil.formatFloatForConfig(f.maximum, f.maximumFractionalDigits);
				ed.unit = f.unit ?? "";
				ed.type = BindingEditorType.LINEAR;
				foreach (ControlAction ha in f.actions) {
					var he = ControlActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.actions.Add(he);
				}
				break;
			case BindingLinf lf:
				ed.name = lf.name;
				ed.address = lf.address;
				ed.minimum = ContinuousFloatUtil.formatFloatForConfig(lf.minimum, lf.minimumFractionalDigits);
				ed.maximum = ContinuousFloatUtil.formatFloatForConfig(lf.maximum, lf.maximumFractionalDigits);
				ed.rangeMinimum = ContinuousFloatUtil.formatFloatForConfig(lf.rangeMinimum, ContinuousFloatUtil.fractionalDigitsForValue(lf.rangeMinimum));
				ed.rangeMaximum = ContinuousFloatUtil.formatFloatForConfig(lf.rangeMaximum, ContinuousFloatUtil.fractionalDigitsForValue(lf.rangeMaximum));
				ed.unit = lf.unit ?? "";
				ed.type = BindingEditorType.LINF;
				foreach (ControlAction ha in lf.actions) {
					var he = ControlActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.actions.Add(he);
				}
				break;
			case BindingLogf lg:
				ed.name = lg.name;
				ed.address = lg.address;
				ed.minimum = ContinuousFloatUtil.formatFloatForConfig(lg.minimum, lg.minimumFractionalDigits);
				ed.maximum = ContinuousFloatUtil.formatFloatForConfig(lg.maximum, lg.maximumFractionalDigits);
				ed.rangeMinimum = ContinuousFloatUtil.formatFloatForConfig(lg.rangeMinimum, ContinuousFloatUtil.fractionalDigitsForValue(lg.rangeMinimum));
				ed.rangeMaximum = ContinuousFloatUtil.formatFloatForConfig(lg.rangeMaximum, ContinuousFloatUtil.fractionalDigitsForValue(lg.rangeMaximum));
				ed.unit = lg.unit ?? "";
				ed.type = BindingEditorType.LOGF;
				foreach (ControlAction ha in lg.actions) {
					var he = ControlActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.actions.Add(he);
				}
				break;
			case BindingLevel lv:
				ed.name = lv.name;
				ed.address = lv.address;
				ed.minimum = ContinuousFloatUtil.formatFloatForConfig(lv.minimum, lv.minimumFractionalDigits);
				ed.maximum = ContinuousFloatUtil.formatFloatForConfig(lv.maximum, lv.maximumFractionalDigits);
				ed.type = BindingEditorType.LEVEL;
				foreach (ControlAction ha in lv.actions) {
					var he = ControlActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.actions.Add(he);
				}
				break;
			case BindingToggle t:
				ed.name = t.name;
				ed.address = t.address;
				ed.type = BindingEditorType.TOGGLE;
				foreach (ControlAction ha in t.actions) {
					var he = ControlActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.actions.Add(he);
				}
				break;
			default:
				throw new InvalidOperationException("Unknown binding type.");
		}
		return ed;
	}

	public ControlActionEditor createActionEditor() {
		var he = new ControlActionEditor { owner = this };
		he.refreshChoiceFromOwner();
		if (he.selectedChoice == null && actionChoices.Count > 0)
			he.selectedChoice = actionChoices[0];
		return he;
	}

	void recomputeValidation() {
		if (_isDeleted) {
			clearValidationErrors();
			return;
		}

		_nameResult = BindingManager.Config.parseBindingNameField(_name);
		_addressResult = BindingManager.Config.parseOscAddressField(_address);
		setValidationErrors(nameof(name), validationMessages(_nameResult));
		setValidationErrors(nameof(address), validationMessages(_addressResult));

		if (showsMinMax)
			validateMinMaxFields();
		else {
			setValidationErrors(nameof(minimum), []);
			setValidationErrors(nameof(maximum), []);
		}

		if (showsRange)
			validateRangeFields();
		else {
			setValidationErrors(nameof(rangeMinimum), []);
			setValidationErrors(nameof(rangeMaximum), []);
		}

		_unitResult = BindingManager.Config.parseUnitField(_unit);
		setValidationErrors(nameof(unit), showsUnit ? validationMessages(_unitResult) : []);
	}

	void validateMinMaxFields() {
		_minimumResult = BindingManager.Config.parseContinuousFloatField(_minimum);
		_maximumResult = BindingManager.Config.parseContinuousFloatField(_maximum);
		var minErrors = validationMessages(_minimumResult).ToList();
		var maxErrors = validationMessages(_maximumResult).ToList();

		if (_minimumResult.isSuccess && _maximumResult.isSuccess) {
			float min = _minimumResult.value.value;
			float max = _maximumResult.value.value;
			if (min > max)
				maxErrors.Add("Maximum must be greater than or equal to minimum.");
			if (type == BindingEditorType.LOGF) {
				if (min <= 0f)
					minErrors.Add("logf minimum must be positive.");
				if (max <= 0f)
					maxErrors.Add("logf maximum must be positive.");
			}
		}

		setValidationErrors(nameof(minimum), minErrors);
		setValidationErrors(nameof(maximum), maxErrors);
	}

	void validateRangeFields() {
		bool rangeMinBlank = string.IsNullOrWhiteSpace(_rangeMinimum);
		bool rangeMaxBlank = string.IsNullOrWhiteSpace(_rangeMaximum);
		var rangeMinErrors = new List<string>();
		var rangeMaxErrors = new List<string>();

		if (rangeMinBlank && rangeMaxBlank) {
			setValidationErrors(nameof(rangeMinimum), []);
			setValidationErrors(nameof(rangeMaximum), []);
			return;
		}
		if (rangeMinBlank || rangeMaxBlank) {
			string message = "Range min and range max must both be set, or both blank.";
			if (rangeMinBlank)
				rangeMinErrors.Add(message);
			if (rangeMaxBlank)
				rangeMaxErrors.Add(message);
		}

		_rangeMinimumResult = BindingManager.Config.parseContinuousFloatField(_rangeMinimum);
		_rangeMaximumResult = BindingManager.Config.parseContinuousFloatField(_rangeMaximum);
		if (!rangeMinBlank)
			rangeMinErrors.AddRange(validationMessages(_rangeMinimumResult));
		if (!rangeMaxBlank)
			rangeMaxErrors.AddRange(validationMessages(_rangeMaximumResult));

		if (_rangeMinimumResult.isSuccess && _rangeMaximumResult.isSuccess) {
			float rangeMin = _rangeMinimumResult.value.value;
			float rangeMax = _rangeMaximumResult.value.value;
			if (rangeMin > rangeMax)
				rangeMaxErrors.Add("Range max must be greater than or equal to range min.");
			if (type == BindingEditorType.LOGF) {
				if (rangeMin <= 0f)
					rangeMinErrors.Add("logf range min must be positive.");
				if (rangeMax <= 0f)
					rangeMaxErrors.Add("logf range max must be positive.");
			}
		}

		setValidationErrors(nameof(rangeMinimum), rangeMinErrors);
		setValidationErrors(nameof(rangeMaximum), rangeMaxErrors);
	}
}

file static class BindingEditorStatics {
	internal static readonly IReadOnlyList<ControlAction> linearPrototypes = new BindingLinear().availableActionPrototypes;
	internal static readonly IReadOnlyList<ControlAction> x32FloatPrototypes = new BindingLinf().availableActionPrototypes;
	internal static readonly IReadOnlyList<ControlAction> togglePrototypes = new BindingToggle().availableActionPrototypes;
}

public sealed class ControlActionEditor : ObservableValidationObject {
	Type _selectedActionType = typeof(ControlActionContinuousDelta);
	HotkeyGesture _hotkey = HotkeyGesture.None;
	bool _isHotkeyCaptureActive;
	string _floatValue = "0";
	bool _boolValue;
	bool _isDeleted;
	bool _longPress;
	ControlActionChoice? _selectedChoice;
	BindingEditor? _owner;
	Result<BindingManager.Config.FloatFieldValue> _floatValueResult = BindingManager.Config.parseContinuousFloatField("0");

	public BindingEditor? owner {
		get => _owner;
		set {
			if (setProperty(ref _owner, value)) {
				raisePropertyChanged(nameof(choiceList));
				refreshChoiceFromOwner();
				recomputeValidation();
			}
		}
	}

	public IReadOnlyList<ControlActionChoice>? choiceList => owner?.actionChoices;

	public ControlActionChoice? selectedChoice {
		get => _selectedChoice;
		set {
			if (!setProperty(ref _selectedChoice, value))
				return;
			if (value != null) {
				_selectedActionType = value.actionType;
				raisePropertyChanged(nameof(selectedActionType));
				raisePropertyChanged(nameof(showsFloatInput));
				raisePropertyChanged(nameof(floatValueLabel));
				raisePropertyChanged(nameof(showsBoolInput));
				recomputeValidation();
			}
		}
	}

	public Type selectedActionType {
		get => _selectedActionType;
		set {
			if (!setProperty(ref _selectedActionType, value))
				return;
			raisePropertyChanged(nameof(showsFloatInput));
			raisePropertyChanged(nameof(floatValueLabel));
			raisePropertyChanged(nameof(showsBoolInput));
			recomputeValidation();
		}
	}

	public HotkeyGesture hotkey {
		get => _hotkey;
		set {
			if (setProperty(ref _hotkey, HotkeyUtil.normalize(value))) {
				raisePropertyChanged(nameof(hotkeyText));
				raisePropertyChanged(nameof(isHotkeyIdlePlaceholder));
			}
		}
	}

	public bool isHotkeyCaptureActive {
		get => _isHotkeyCaptureActive;
		set {
			if (setProperty(ref _isHotkeyCaptureActive, value)) {
				raisePropertyChanged(nameof(hotkeyText));
				raisePropertyChanged(nameof(isHotkeyIdlePlaceholder));
			}
		}
	}

	public string floatValue {
		get => _floatValue;
		set {
			if (setTextProperty(ref _floatValue, value)) {
				raisePropertyChanged(nameof(floatValueLabel));
				recomputeValidation();
			}
		}
	}

	internal void raiseFloatValueLabelChanged() => raisePropertyChanged(nameof(floatValueLabel));

	public string floatValueLabel {
		get {
			if (owner == null)
				return "";
			if (selectedActionType == typeof(ControlActionContinuousRawDelta))
				return "";
			string u = owner.unit;
			if (string.IsNullOrWhiteSpace(u))
				return "";
			return u.Trim();
		}
	}

	public bool boolValue {
		get => _boolValue;
		set => setProperty(ref _boolValue, value);
	}

	public bool isDeleted {
		get => _isDeleted;
		set {
			if (!setProperty(ref _isDeleted, value))
				return;
			raisePropertyChanged(nameof(isNotDeleted));
			recomputeValidation();
		}
	}

	public bool longPress {
		get => _longPress;
		set => setProperty(ref _longPress, value);
	}

	public bool isNotDeleted => !_isDeleted;

	public string hotkeyText {
		get {
			if (!hotkey.isNone)
				return HotkeyUtil.format(hotkey);
			return _isHotkeyCaptureActive ? "Press key, then release…" : "Set hotkey";
		}
	}

	public bool isHotkeyIdlePlaceholder => hotkey.isNone && !_isHotkeyCaptureActive;

	public bool showsFloatInput => valueKindForActionType(selectedActionType) == ControlActionValueKind.FLOAT;

	public bool showsBoolInput => valueKindForActionType(selectedActionType) == ControlActionValueKind.BOOL;

	public IEnumerable<ValidationFieldDescriptor> footerValidationFields {
		get {
			if (showsFloatInput)
				yield return new(nameof(floatValue), "Value");
		}
	}

	public void refreshChoiceFromOwner() {
		if (owner == null) {
			_selectedChoice = null;
			raisePropertyChanged(nameof(selectedChoice));
			return;
		}
		_selectedChoice = owner.actionChoices.FirstOrDefault(c => c.actionType == _selectedActionType)
			?? owner.actionChoices.FirstOrDefault();
		if (_selectedChoice != null)
			_selectedActionType = _selectedChoice.actionType;
		raisePropertyChanged(nameof(selectedChoice));
		raisePropertyChanged(nameof(selectedActionType));
		raisePropertyChanged(nameof(showsFloatInput));
		raisePropertyChanged(nameof(floatValueLabel));
		raisePropertyChanged(nameof(showsBoolInput));
		raisePropertyChanged(nameof(choiceList));
		recomputeValidation();
	}

	public bool isCompatibleWithBindingType(BindingEditorType bindingType) {
		if (bindingType == BindingEditorType.TOGGLE)
			return typeof(ControlActionToggleAbstract).IsAssignableFrom(selectedActionType);
		return typeof(ControlActionContinuousAbstract).IsAssignableFrom(selectedActionType);
	}

	/// <summary>Bound to row Visibility; cached because instantiating a prototype per read is wasteful.</summary>
	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, ControlActionValueKind?> _valueKindByActionType = new();

	static ControlActionValueKind? valueKindForActionType(Type t) =>
		_valueKindByActionType.GetOrAdd(t, static type => tryInstantiatePrototype(type)?.valueKind);

	static ControlAction? tryInstantiatePrototype(Type t) {
		try {
			return (ControlAction)Activator.CreateInstance(t)!;
		} catch {
			return null;
		}
	}

	public static ControlActionEditor fromAction(ControlAction a) {
		var ed = new ControlActionEditor {
			selectedActionType = a.GetType(),
			hotkey = a.hotkey,
			longPress = a.longPress,
		};
		switch (a) {
			case ControlActionContinuousSet fs:
				ed.floatValue = ContinuousFloatUtil.formatFloatForConfig(fs.value, fs.fractionalDigits);
				break;
			case ControlActionContinuousDelta fd:
				ed.floatValue = ContinuousFloatUtil.formatFloatForConfig(fd.delta, fd.fractionalDigits);
				break;
			case ControlActionContinuousRawDelta rd:
				ed.floatValue = ContinuousFloatUtil.formatFloatForConfig(rd.delta, rd.fractionalDigits);
				break;
			case ControlActionToggleSet ts:
				ed.boolValue = ts.on;
				break;
		}
		return ed;
	}

	public bool tryBuildModel(BindingEditorType bindingType, [NotNullWhen(true)] out ControlAction? action, out string? error) {
		action = null;
		error = null;
		if (!isCompatibleWithBindingType(bindingType)) {
			error = "Action type does not match binding type.";
			return false;
		}
		if (hotkey.isNone) {
			error = "Hotkey is required.";
			return false;
		}
		if (!HotkeyUtil.tryValidate(hotkey, out UiTextFeedback hkFb)) {
			error = hkFb.text;
			return false;
		}

		string floatText = floatValue.Trim();
		int frac = ContinuousFloatUtil.fractionalDigitsOfTypedString(floatText);

		switch (Activator.CreateInstance(selectedActionType)) {
			case ControlActionContinuousSet fs:
				if (!float.TryParse(floatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || !float.IsFinite(v)) {
					error = "Set value must be a finite number.";
					return false;
				}
				fs.value = ContinuousFloatUtil.RoundToBindingDecimals(v);
				fs.fractionalDigits = frac;
				fs.hotkey = HotkeyUtil.normalize(hotkey);
				fs.longPress = longPress;
				action = fs;
				return true;
			case ControlActionContinuousDelta fd:
				if (!float.TryParse(floatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float d) || !float.IsFinite(d)) {
					error = "Delta must be a finite number.";
					return false;
				}
				fd.delta = ContinuousFloatUtil.RoundToBindingDecimals(d);
				fd.fractionalDigits = frac;
				fd.hotkey = HotkeyUtil.normalize(hotkey);
				fd.longPress = longPress;
				action = fd;
				return true;
			case ControlActionContinuousRawDelta fr:
				if (!float.TryParse(floatText, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) || !float.IsFinite(r)) {
					error = "Raw delta must be a finite number.";
					return false;
				}
				fr.delta = ContinuousFloatUtil.RoundToBindingDecimals(r);
				fr.fractionalDigits = frac;
				fr.hotkey = HotkeyUtil.normalize(hotkey);
				fr.longPress = longPress;
				action = fr;
				return true;
			case ControlActionToggleSet ts:
				ts.on = boolValue;
				ts.hotkey = HotkeyUtil.normalize(hotkey);
				ts.longPress = longPress;
				action = ts;
				return true;
			case ControlActionToggleFlip tf:
				tf.hotkey = HotkeyUtil.normalize(hotkey);
				tf.longPress = longPress;
				action = tf;
				return true;
			default:
				error = "Unknown action type.";
				return false;
		}
	}

	void recomputeValidation() {
		if (_isDeleted || !showsFloatInput) {
			setValidationErrors(nameof(floatValue), []);
			return;
		}

		_floatValueResult = BindingManager.Config.parseContinuousFloatField(_floatValue);
		setValidationErrors(nameof(floatValue), validationMessages(_floatValueResult));
	}
}
