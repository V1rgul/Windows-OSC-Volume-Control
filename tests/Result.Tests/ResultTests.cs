namespace Result.Tests;

public sealed class ValidationError : Result.Error
{
	public string message { get; init; } = "";
}

public class ResultImplicitConversionTests
{
	[Fact]
	public void implicitValue_isSuccess()
	{
		Result<int> result = 42;

		Assert.True(result.isSuccess);
		Assert.False(result.isError);
		Assert.Equal(42, result.value);
		Assert.Empty(result.errors);
	}

	[Fact]
	public void implicitError_isError()
	{
		var error = new ValidationError { message = "bad" };
		Result<int> result = error;

		Assert.True(result.isError);
		Assert.Single(result.errors);
		Assert.Same(error, result.errors[0]);
	}

	[Fact]
	public void unspecifiedError_isError()
	{
		Result<int> result = Result.unspecifiedError;

		Assert.True(result.isError);
		Assert.Single(result.errors);
		Assert.Same(Result.unspecifiedError, result.errors[0]);
		Assert.IsType<Result.Error.Unspecified>(result.errors[0]);
	}

	[Fact]
	public void implicitErrorArray_isError()
	{
		Result.Error[] errors =
		[
			new ValidationError { message = "a" },
			new ValidationError { message = "b" },
		];
		Result<int> result = errors;

		Assert.True(result.isError);
		Assert.Equal(2, result.errors.Length);
		Assert.Same(errors, result.errors);
	}

	[Fact]
	public void implicitEmptyErrorArray_throws()
	{
		Assert.Throws<ArgumentException>(() =>
		{
			Result<int> result = Array.Empty<Result.Error>();
			_ = result;
		});
	}
}

public class ResultMatchTests
{
	[Fact]
	public void match_onValue()
	{
		Result<int> result = 7;
		var next = result.match(v => v * 2, _ => -1);

		Assert.Equal(14, next);
	}

	[Fact]
	public void match_onError()
	{
		Result<int> result = new ValidationError { message = "x" };
		var next = result.match(_ => 0, errors => errors.Length);

		Assert.Equal(1, next);
	}

	[Fact]
	public async Task matchAsync_onValue()
	{
		Result<int> result = 3;
		var next = await result.matchAsync(
			async v =>
			{
				await Task.Yield();
				return v + 1;
			},
			async _ =>
			{
				await Task.Yield();
				return -1;
			});

		Assert.Equal(4, next);
	}

	[Fact]
	public async Task matchAsync_onError()
	{
		Result<int> result = Result.unspecifiedError;
		var next = await result.matchAsync(
			async _ =>
			{
				await Task.Yield();
				return 0;
			},
			async errors =>
			{
				await Task.Yield();
				return errors.Length;
			});

		Assert.Equal(1, next);
	}
}

public class ResultSwitchTests
{
	[Fact]
	public void switch_invokesCorrectBranch()
	{
		Result<int> success = 1;
		var valueSeen = 0;
		success.@switch(v => valueSeen = v, _ => valueSeen = -1);
		Assert.Equal(1, valueSeen);

		Result<int> failure = Result.unspecifiedError;
		var errorCount = 0;
		failure.@switch(_ => errorCount = -1, errors => errorCount = errors.Length);
		Assert.Equal(1, errorCount);
	}

	[Fact]
	public async Task switchAsync_invokesCorrectBranch()
	{
		Result<int> failure = new ValidationError();
		var touched = false;
		await failure.switchAsync(
			async _ =>
			{
				await Task.Yield();
				touched = false;
			},
			async _ =>
			{
				await Task.Yield();
				touched = true;
			});
		Assert.True(touched);
	}
}

public class ResultFunctionReturnTests
{
	[Fact]
	public void createUser_returnsTransparently()
	{
		Assert.True(createUser("").isError);
		Assert.True(createUser("x").isError);
		Assert.True(createUser(new string('a', 101)).isError);
		Assert.True(createUser("alice").isSuccess);
	}

	static Result<string> createUser(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return new ValidationError { message = "empty" };

		if (name.Length < 2)
		{
			Result.Error[] errors = [new ValidationError { message = "too short" }];
			return errors;
		}

		if (name.Length > 100)
			return new ValidationError[] { new ValidationError { message = "too long" } };

		return name;
	}
}
