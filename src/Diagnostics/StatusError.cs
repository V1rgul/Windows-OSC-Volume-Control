namespace WindowsOscVolumeControl.Diagnostics;

public abstract partial record StatusError {
	public abstract record Generic : StatusError {
		public sealed record Starting : Generic;
	}
}
