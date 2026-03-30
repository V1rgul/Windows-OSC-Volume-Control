namespace WindowsOscVolumeControl;

public abstract class OscBinding {
	public string name { get; set; } = "";
	public string address { get; set; } = "";

	protected OscBinding() { }

	protected OscBinding(OscBinding other) {
		ArgumentNullException.ThrowIfNull(other);
		name = other.name;
		address = other.address;
	}

	/// <summary>Label for OSD / tray: <see cref="name"/> when set, otherwise <see cref="address"/> (required for a valid binding).</summary>
	public string displayName() {
		if (!string.IsNullOrWhiteSpace(name)) return name;
		return address;
	}
}
