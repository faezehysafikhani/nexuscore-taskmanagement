namespace NexusCore.Domain.Identity;

/// <summary>What kind of account a user is. Guests and service accounts are not counted against an organization's user limit.</summary>
public enum UserKind
{
    Normal = 0,

    /// <summary>An outside person (a client, an adviser): an ordinary sign-in with only the access an administrator gives.</summary>
    Guest = 1,

    /// <summary>A non-human account for another system; it has no password and signs in with an API key.</summary>
    ServiceAccount = 2
}
