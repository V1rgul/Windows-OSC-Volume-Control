namespace WindowsOscVolumeControl;

public abstract partial record Error {
	public abstract partial record Generic : Error {
		public sealed record Starting : Generic;
	}
}
