namespace DisputePortal.Api.Domain;

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string email, string displayName, string passwordHash, string role, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public string Role { get; private set; } = Roles.Customer;

    public DateTimeOffset CreatedAt { get; private set; }

    public void ChangePasswordHash(string passwordHash) => PasswordHash = passwordHash;
}
