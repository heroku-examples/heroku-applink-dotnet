namespace Heroku.Applink.Models;

/// <summary>
/// Authenticated user context returned with the org authorization.
/// </summary>
public sealed class User
{
    /// <summary>Salesforce user Id.</summary>
    public string Id { get; }
    /// <summary>Salesforce username.</summary>
    public string Username { get; }
    /// <summary>Creates a new user context.</summary>
    public User(string id, string username)
    {
        Id = id;
        Username = username;
    }
}
