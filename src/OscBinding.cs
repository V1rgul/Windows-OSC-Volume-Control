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
}
