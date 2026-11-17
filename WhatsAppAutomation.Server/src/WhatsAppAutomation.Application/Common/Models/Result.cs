namespace WhatsAppAutomation.Application.Common.Models;

/// <summary>An expected failure: a stable machine code plus a message that is safe to show.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind)
{
    public static Error Validation(string message) => new("validation_failed", message, ErrorKind.Validation);
}

public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    TooManyRequests,

    /// <summary>A service we depend on (such as the WhatsApp gateway) cannot be reached.</summary>
    Unavailable,
}

/// <summary>
/// Lets generic code (such as the validation pipeline) build a failed result of any result type
/// without knowing which one it is. Uses a C# 11 "static abstract" interface member.
/// </summary>
public interface IFailable<out TSelf>
{
    Error? Error { get; }

    static abstract TSelf Fail(Error error);
}

public readonly struct Result<T> : IFailable<Result<T>>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = null;
    }

    private Result(Error error)
    {
        _value = default;
        Error = error;
    }

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Fail(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Fail(error);
}

public readonly struct Result : IFailable<Result>
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Fail(Error error) => new(error);

    public static implicit operator Result(Error error) => Fail(error);
}
