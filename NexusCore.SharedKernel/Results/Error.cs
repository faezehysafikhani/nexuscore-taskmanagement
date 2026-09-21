namespace NexusCore.SharedKernel.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
    public static Error Validation(string message) => new("validation.error", message);
    public static Error NotFound(string message) => new("not_found", message);
    public static Error Conflict(string message) => new("conflict", message);
    public static Error Unauthorized(string message = "Authentication is required.") => new("unauthorized", message);

    /// <summary>Signed in, but not allowed to do this (HTTP 403). Not for missing sign-in - that is Unauthorized.</summary>
    public static Error Forbidden(string message = "You are not allowed to perform this action.") => new("forbidden", message);
}
