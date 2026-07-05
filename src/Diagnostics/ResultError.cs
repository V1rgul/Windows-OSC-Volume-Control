namespace WindowsOscVolumeControl.Diagnostics;

public abstract class ResultErrorWithMsg : global::Result.Result.Error
{
	public required string message { get; init; }
	public override string ToString() => message;
}

public static partial class ResultError
{
	public abstract class Generic : ResultErrorWithMsg
	{
		public sealed class Parsing : Generic
		{
			public const string DEFAULT_MESSAGE = "Invalid value.";
			public override string ToString() => string.IsNullOrWhiteSpace(message) ? DEFAULT_MESSAGE : message;
		}
	}
}
