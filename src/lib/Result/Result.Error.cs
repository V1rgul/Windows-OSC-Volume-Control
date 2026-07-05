namespace Result;

public static class Result
{
	public static readonly Error unspecifiedError = new Error.Unspecified();

	public abstract class Error
	{
		public sealed class Unspecified : Error;
	}
}
