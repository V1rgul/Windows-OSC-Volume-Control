using System.Diagnostics.CodeAnalysis;

namespace Result;

public interface IResult
{
	bool isError { get; }
	bool isSuccess { get; }
	Result.Error[] errors { get; }
}

public interface IResult<TValue> : IResult
{
	TValue value { get; }
}

public readonly record struct Result<TValue> : IResult<TValue>
{
	readonly TValue? _value;
	readonly Result.Error[]? _errors;

	Result(TValue value) => _value = value;

	Result(Result.Error error) => _errors = [error];

	Result(Result.Error[] errors)
	{
		if (errors.Length == 0)
			throw new ArgumentException("Error array cannot be empty.", nameof(errors));
		_errors = errors;
	}

	Result(IEnumerable<Result.Error> errors) : this([.. errors]) { }

	[MemberNotNullWhen(true, nameof(_errors))]
	[MemberNotNullWhen(true, nameof(errors))]
	[MemberNotNullWhen(false, nameof(value))]
	[MemberNotNullWhen(false, nameof(_value))]
	public bool isError => _errors is not null;

	[MemberNotNullWhen(false, nameof(_errors))]
	[MemberNotNullWhen(false, nameof(errors))]
	[MemberNotNullWhen(true, nameof(value))]
	[MemberNotNullWhen(true, nameof(_value))]
	public bool isSuccess => _errors is null;

	public TValue value
	{
		get
		{
			ArgumentNullException.ThrowIfNull(_value);
			return _value;
		}
	}
	public Result.Error[] errors
	{
		get
		{
			ArgumentNullException.ThrowIfNull(_errors);
			return _errors;
		}
	}

	public TNext match<TNext>(Func<TValue, TNext> onValue, Func<Result.Error[], TNext> onError)
	{
		if (isError)
			return onError(errors);
		return onValue(value);
	}

	public async Task<TNext> matchAsync<TNext>(
		Func<TValue, Task<TNext>> onValue,
		Func<Result.Error[], Task<TNext>> onError)
	{
		if (isError)
			return await onError(errors).ConfigureAwait(false);
		return await onValue(value).ConfigureAwait(false);
	}

	public void @switch(Action<TValue> onValue, Action<Result.Error[]> onError)
	{
		if (isError)
		{
			onError(errors);
			return;
		}
		onValue(value);
	}

	public async Task switchAsync(Func<TValue, Task> onValue, Func<Result.Error[], Task> onError)
	{
		if (isError)
		{
			await onError(errors).ConfigureAwait(false);
			return;
		}
		await onValue(value).ConfigureAwait(false);
	}

	public static implicit operator Result<TValue>(TValue value) => new(value);
	public static implicit operator Result<TValue>(Result.Error error) => new(error);
	public static implicit operator Result<TValue>(Result.Error[] errors) => new(errors);
}
