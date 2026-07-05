namespace WindowsOscVolumeControl.Diagnostics;

public abstract class ResultErrorWithMsg : global::Result.Result.Error
{
	public required string message { get; init; }
}

public static partial class ResultError
{
	public abstract class Generic : ResultErrorWithMsg
	{
		public sealed class Parsing : Generic;
	}
}
