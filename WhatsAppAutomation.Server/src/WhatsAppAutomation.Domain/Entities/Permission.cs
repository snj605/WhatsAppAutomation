using WhatsAppAutomation.Domain.Authorization;
namespace WhatsAppAutomation.Domain.Entities;

/// <summary>A row of the permissions table. Filled from <see cref="Permissions.All"/> by migrations.</summary>
public sealed class Permission
{
    private Permission()
    {
        Name = string.Empty;
        Group = string.Empty;
        Description = string.Empty;
    }

    public Permission(PermissionDefinition definition)
    {
        Name = definition.Name;
        Group = definition.Group;
        Description = definition.Description;
    }

    public string Name { get; private set; }

    public string Group { get; private set; }

    public string Description { get; private set; }
}
