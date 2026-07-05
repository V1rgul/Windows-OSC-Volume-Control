namespace WindowsOscVolumeControl.Diagnostics;

public abstract partial record StatusError {
	public static bool isType<TStatusError>(Type statusErrorType)
		where TStatusError : StatusError =>
		typeof(TStatusError).IsAssignableFrom(statusErrorType);

	public abstract record Generic : StatusError {
		public sealed record Starting : Generic;
	}
}
