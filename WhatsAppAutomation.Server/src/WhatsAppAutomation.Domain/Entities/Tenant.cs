namespace WhatsAppAutomation.Domain.Entities;

/// <summary>
/// A customer of the platform (a business or a family), with its own admins and members. Each person in it
/// still has their own WhatsApp number and their own contacts.
/// </summary>
public sealed class Tenant
{
    public const int NameMaxLength = 100;

    private Tenant()
    {
    }

    public Tenant(string name, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CreatedAt = now;
        Rename(name);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>A turned-off tenant: nobody in it can sign in, and nothing is sent for it.</summary>
    public bool IsActive { get; private set; } = true;

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name) => Name = name.Trim();

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>One switch a tenant admin turned on for a member, e.g. "broadcasts.send".</summary>
public sealed class UserSwitch
{
    private UserSwitch()
    {
    }

    public UserSwitch(Guid userId, string permission)
    {
        UserId = userId;
        Permission = permission;
    }

    public Guid UserId { get; private set; }

    public string Permission { get; private set; } = string.Empty;
}
