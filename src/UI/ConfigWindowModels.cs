using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace WindowsOscVolumeControl;

public enum BindingEditorType {
	FADER,
	TOGGLE,
}

public sealed class BindingTypeUiChoice(BindingEditorType value, string label) {
	public BindingEditorType value { get; } = value;
	public string label { get; } = label;
}

public sealed class HotkeyActionChoice(Type actionType, string displayName) {
	public Type actionType { get; } = actionType;
	public string displayName { get; } = displayName;
}

public abstract class ObservableObject : INotifyPropertyChanged {
	public event PropertyChangedEventHandler? PropertyChanged;

	protected bool setProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null) {
		if (EqualityComparer<T>.Default.Equals(field, value))
			return false;

		field = value;
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		return true;
	}

	protected void raisePropertyChanged([CallerMemberName] string? propertyName = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class BindingEditor : ObservableObject {
	BindingEditorType _type = BindingEditorType.FADER;
	string _name = "";
	string _address = "";
	string _minimum = "0";
	string _maximum = "1";
	bool _isDeleted;
	// Stable list instance: ComboBox matches SelectedItem by reference; rebuilding the list each get left the box blank.
	IReadOnlyList<HotkeyActionChoice>? _actionChoices;

	public string name {
		get => _name;
		set => setProperty(ref _name, value ?? "");
	}

	public string address {
		get => _address;
		set => setProperty(ref _address, value ?? "");
	}

	public BindingEditorType type {
		get => _type;
		set {
			if (setProperty(ref _type, value)) {
				rebuildActionChoices();
				raisePropertyChanged(nameof(isFader));
				raisePropertyChanged(nameof(typeDisplayLabel));
				raisePropertyChanged(nameof(actionChoices));
				pruneHotkeysForType();
				foreach (HotkeyActionEditor h in hotkeys)
					h.refreshChoiceFromOwner();
			}
		}
	}

	public string minimum {
		get => _minimum;
		set => setProperty(ref _minimum, value ?? "");
	}

	public string maximum {
		get => _maximum;
		set => setProperty(ref _maximum, value ?? "");
	}

	public bool isDeleted {
		get => _isDeleted;
		set => setProperty(ref _isDeleted, value);
	}

	public ObservableCollection<HotkeyActionEditor> hotkeys { get; } = [];

	public bool isFader => type == BindingEditorType.FADER;

	public string typeDisplayLabel => isFader ? "Fader" : "Toggle";

	public IReadOnlyList<BindingTypeUiChoice> typeChoices { get; } = [
		new(BindingEditorType.FADER, "Fader"),
		new(BindingEditorType.TOGGLE, "Toggle"),
	];

	public IReadOnlyList<HotkeyAction> availableActionPrototypes =>
		isFader ? BindingEditorStatics.faderPrototypes : BindingEditorStatics.togglePrototypes;

	public IReadOnlyList<HotkeyActionChoice> actionChoices {
		get {
			_actionChoices ??= buildActionChoices();
			return _actionChoices;
		}
	}

	void rebuildActionChoices() {
		_actionChoices = buildActionChoices();
	}

	List<HotkeyActionChoice> buildActionChoices() =>
		availableActionPrototypes.Select(static p => new HotkeyActionChoice(p.GetType(), p.name)).ToList();

	void pruneHotkeysForType() {
		for (int i = hotkeys.Count - 1; i >= 0; i--) {
			if (!hotkeys[i].isCompatibleWithBindingType(type))
				hotkeys.RemoveAt(i);
		}
	}

	public static BindingEditor fromBinding(BindingAbstract binding) {
		var ed = new BindingEditor();
		switch (binding) {
			case BindingFader f:
				ed.type = BindingEditorType.FADER;
				ed.name = f.name;
				ed.address = f.address;
				ed.minimum = FaderFloatUtil.FormatGridFloat(f.minimum);
				ed.maximum = FaderFloatUtil.FormatGridFloat(f.maximum);
				foreach (HotkeyAction ha in f.hotkeys) {
					var he = HotkeyActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.hotkeys.Add(he);
				}
				break;
			case BindingToggle t:
				ed.type = BindingEditorType.TOGGLE;
				ed.name = t.name;
				ed.address = t.address;
				foreach (HotkeyAction ha in t.hotkeys) {
					var he = HotkeyActionEditor.fromAction(ha);
					he.owner = ed;
					he.refreshChoiceFromOwner();
					ed.hotkeys.Add(he);
				}
				break;
			default:
				throw new InvalidOperationException("Unknown binding type.");
		}
		return ed;
	}

	public HotkeyActionEditor createHotkeyEditor() {
		var he = new HotkeyActionEditor { owner = this };
		he.refreshChoiceFromOwner();
		if (he.selectedChoice == null && actionChoices.Count > 0)
			he.selectedChoice = actionChoices[0];
		return he;
	}
}

static file class BindingEditorStatics {
	internal static readonly IReadOnlyList<HotkeyAction> faderPrototypes = new BindingFader().availableActionPrototypes;
	internal static readonly IReadOnlyList<HotkeyAction> togglePrototypes = new BindingToggle().availableActionPrototypes;
}

public sealed class HotkeyActionEditor : ObservableObject {
	Type _selectedActionType = typeof(HotkeyActionFaderDelta);
	HotkeyGesture _hotkey = HotkeyGesture.None;
	bool _isHotkeyCaptureActive;
	string _floatValue = "0";
	bool _boolValue;
	bool _isDeleted;
	HotkeyActionChoice? _selectedChoice;
	BindingEditor? _owner;

	public BindingEditor? owner {
		get => _owner;
		set {
			if (setProperty(ref _owner, value)) {
				raisePropertyChanged(nameof(choiceList));
				refreshChoiceFromOwner();
			}
		}
	}

	public IReadOnlyList<HotkeyActionChoice>? choiceList => owner?.actionChoices;

	public HotkeyActionChoice? selectedChoice {
		get => _selectedChoice;
		set {
			if (!setProperty(ref _selectedChoice, value))
				return;
			if (value != null) {
				_selectedActionType = value.actionType;
				raisePropertyChanged(nameof(selectedActionType));
				raisePropertyChanged(nameof(showsFloatInput));
				raisePropertyChanged(nameof(showsBoolInput));
			}
		}
	}

	public Type selectedActionType {
		get => _selectedActionType;
		set {
			if (!setProperty(ref _selectedActionType, value))
				return;
			raisePropertyChanged(nameof(showsFloatInput));
			raisePropertyChanged(nameof(showsBoolInput));
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
		set => setProperty(ref _floatValue, value ?? "");
	}

	public bool boolValue {
		get => _boolValue;
		set => setProperty(ref _boolValue, value);
	}

	public bool isDeleted {
		get => _isDeleted;
		set => setProperty(ref _isDeleted, value);
	}

	public string hotkeyText {
		get {
			if (!hotkey.isNone)
				return HotkeyUtil.format(hotkey);
			return _isHotkeyCaptureActive ? "Press any key…" : "Set hotkey";
		}
	}

	public bool isHotkeyIdlePlaceholder => hotkey.isNone && !_isHotkeyCaptureActive;

	public bool showsFloatInput {
		get {
			HotkeyAction? p = tryInstantiatePrototype(selectedActionType);
			return p?.valueKind == HotkeyActionValueKind.FLOAT;
		}
	}

	public bool showsBoolInput {
		get {
			HotkeyAction? p = tryInstantiatePrototype(selectedActionType);
			return p?.valueKind == HotkeyActionValueKind.BOOL;
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
		raisePropertyChanged(nameof(showsBoolInput));
		raisePropertyChanged(nameof(choiceList));
	}

	public bool isCompatibleWithBindingType(BindingEditorType bindingType) {
		if (bindingType == BindingEditorType.FADER)
			return typeof(HotkeyActionFaderAbstract).IsAssignableFrom(selectedActionType);
		return typeof(HotkeyActionToggleAbstract).IsAssignableFrom(selectedActionType);
	}

	static HotkeyAction? tryInstantiatePrototype(Type t) {
		try {
			return (HotkeyAction)Activator.CreateInstance(t)!;
		} catch {
			return null;
		}
	}

	public static HotkeyActionEditor fromAction(HotkeyAction a) {
		var ed = new HotkeyActionEditor {
			selectedActionType = a.GetType(),
			hotkey = a.hotkey,
		};
		switch (a) {
			case HotkeyActionFaderSet fs:
				ed.floatValue = FaderFloatUtil.FormatGridFloat(fs.value);
				break;
			case HotkeyActionFaderDelta fd:
				ed.floatValue = FaderFloatUtil.FormatGridFloat(fd.delta);
				break;
			case HotkeyActionToggleSet ts:
				ed.boolValue = ts.on;
				break;
		}
		return ed;
	}

	public bool tryBuildModel(BindingEditorType bindingType, [NotNullWhen(true)] out HotkeyAction? action, out string? error) {
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
		if (!HotkeyUtil.tryValidate(hotkey, out string hkErr)) {
			error = hkErr;
			return false;
		}

		switch (Activator.CreateInstance(selectedActionType)) {
			case HotkeyActionFaderSet fs:
				if (!float.TryParse(floatValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || !float.IsFinite(v)) {
					error = "Set value must be a finite number.";
					return false;
				}
				fs.value = FaderFloatUtil.RoundToBindingDecimals(v);
				fs.hotkey = HotkeyUtil.normalize(hotkey);
				action = fs;
				return true;
			case HotkeyActionFaderDelta fd:
				if (!float.TryParse(floatValue.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) || !float.IsFinite(d)) {
					error = "Delta must be a finite number.";
					return false;
				}
				fd.delta = FaderFloatUtil.RoundToBindingDecimals(d);
				fd.hotkey = HotkeyUtil.normalize(hotkey);
				action = fd;
				return true;
			case HotkeyActionToggleSet ts:
				ts.on = boolValue;
				ts.hotkey = HotkeyUtil.normalize(hotkey);
				action = ts;
				return true;
			case HotkeyActionToggleFlip tf:
				tf.hotkey = HotkeyUtil.normalize(hotkey);
				action = tf;
				return true;
			default:
				error = "Unknown action type.";
				return false;
		}
	}
}
