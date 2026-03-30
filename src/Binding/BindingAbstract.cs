namespace WindowsOscVolumeControl;

public abstract class BindingAbstract {
	private string _name = "";
	private string _address = "";
	private string _displayName;

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

	protected BindingAbstract() {
		refreshDisplayName();
	}

	protected BindingAbstract(BindingAbstract other) {
		ArgumentNullException.ThrowIfNull(other);
		_name = other._name;
		_address = other._address;
		refreshDisplayName();
	}

	private void refreshDisplayName() {
		_displayName = string.IsNullOrWhiteSpace(_name) ? _address : _name;
	}
}
