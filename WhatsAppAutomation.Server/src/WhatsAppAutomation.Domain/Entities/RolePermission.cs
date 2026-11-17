using WhatsAppAutomation.Domain.Authorization;
namespace WhatsAppAutomation.Domain.Entities;

/// <summary>Grants one permission to one role.</summary>
public sealed class RolePermission
{
    private RolePermission()
    {
        Permission = string.Empty;
    }

    public RolePermission(Guid roleId, string permission)
    {
        if (!Permissions.IsKnown(permission))
        {
            throw new ArgumentException($"Unknown permission '{permission}'.", nameof(permission));
        }

        RoleId = roleId;
        Permission = permission;
    }

    public Guid RoleId { get; private set; }

    public string Permission { get; private set; }
}
