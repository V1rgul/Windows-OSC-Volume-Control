namespace WindowsOscVolumeControl.Diagnostics;

public abstract partial record Error {
	public abstract record Generic : Error {
		public sealed record Starting : Generic;
	}
}
