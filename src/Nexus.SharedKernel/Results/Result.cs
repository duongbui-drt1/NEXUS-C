namespace Nexus.SharedKernel.Results;

public enum ErrorType
{
    Validation = 400,
    Unauthorized = 401,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
    UnprocessableEntity = 422,
    TooManyRequests = 429,
    Failure = 500
}

public sealed record Error(
    string Code,
    string Message,
    ErrorType Type = ErrorType.Failure,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Validation);

    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? details = null)
        => new(code, message, ErrorType.Validation, details);

    public static Error Unauthorized(string code, string message)
        => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message)
        => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message)
        => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message)
        => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message)
        => new(code, message, ErrorType.UnprocessableEntity);

    public static Error TooManyRequests(string code, string message)
        => new(code, message, ErrorType.TooManyRequests);

    public static Error Failure(string code, string message)
        => new(code, message, ErrorType.Failure);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("Successful result cannot contain an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("Failure result must contain an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access Value of a failed Result.");

    public static implicit operator Result<TValue>(TValue value) => Success(value);
    public static implicit operator Result<TValue>(Error error) => Failure<TValue>(error);
}

public sealed class DomainException : Exception
{
    public Error Error { get; }

    public DomainException(Error error)
        : base(error.Message)
    {
        Error = error;
    }
}
