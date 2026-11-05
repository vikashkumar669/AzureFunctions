namespace Harbourline.Connect.Core.Common;

/// <summary>How a business operation ended, independent of HTTP.</summary>
public enum OutcomeStatus
{
    Ok,
    Created,
    NotFound,
    Conflict,
    PreconditionFailed,
    Invalid,
}

// <listing:outcome>
/// <summary>
/// The result of a service call. Core code returns an Outcome; only the
/// function layer turns it into an HTTP status (Chapter 7).
/// </summary>
public sealed record Outcome<T>(
    OutcomeStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Detail = null)
{
    public static Outcome<T> Ok(T value) => new(OutcomeStatus.Ok, value);
    public static Outcome<T> Created(T value) =>
        new(OutcomeStatus.Created, value);
    public static Outcome<T> NotFound(string detail) =>
        new(OutcomeStatus.NotFound, Detail: detail);
    public static Outcome<T> Conflict(string detail) =>
        new(OutcomeStatus.Conflict, Detail: detail);
    public static Outcome<T> PreconditionFailed(string detail) =>
        new(OutcomeStatus.PreconditionFailed, Detail: detail);
    public static Outcome<T> Invalid(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(OutcomeStatus.Invalid, Errors: errors);
}
// </listing:outcome>
