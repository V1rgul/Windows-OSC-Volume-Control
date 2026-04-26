namespace WindowsOscVolumeControl;

public abstract class BindingAbstract {
	private string _name = "";
	private string _address = "";
	private string _displayName = "";

	public string name {
		get => _name;
		set {
			_name = value ?? "";
			refreshDisplayName();
		}
	}

	public string address {
		get => _address;
		set {
			_address = value ?? "";
			refreshDisplayName();
		}
	}

	/// <summary>Label for OSD / tray: <see cref="name"/> when set, otherwise <see cref="address"/> (required for a valid binding). Updated when those properties change.</summary>
	public string displayName => _displayName;

	/// <summary>Control actions (hotkey + payload) for this binding.</summary>
	public List<ControlAction> actions { get; set; } = [];

	/// <summary>Prototype instances for the settings UI action picker (hotkey may be <see cref="HotkeyGesture.None"/>).</summary>
	public abstract IReadOnlyList<ControlAction> availableActionPrototypes { get; }

	protected BindingAbstract() {
		refreshDisplayName();
	}

	protected BindingAbstract(BindingAbstract other) {
		ArgumentNullException.ThrowIfNull(other);
		_name = other._name;
		_address = other._address;
		actions = other.actions.Select(static h => h.clone()).ToList();
		refreshDisplayName();
	}

	private void refreshDisplayName() {
		_displayName = string.IsNullOrWhiteSpace(_name) ? _address : _name;
	}
}
