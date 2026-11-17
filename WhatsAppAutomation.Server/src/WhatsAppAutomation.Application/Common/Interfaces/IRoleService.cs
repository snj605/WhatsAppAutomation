namespace WhatsAppAutomation.Application.Common.Interfaces;

/// <summary>A role with what it grants and how many users hold it.</summary>
public sealed record RoleDetails(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount);

/// <summary>Role storage. Business rules (who may change what) live in the handlers, not here.</summary>
public interface IRoleService
{
    Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken);

    Task<RoleDetails?> FindAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>The ids from this list that exist.</summary>
    Task<IReadOnlyList<RoleDetails>> FindManyAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    /// <summary>Whether another role already uses this name (names are compared without case).</summary>
    Task<bool> NameExistsAsync(string name, Guid? exceptRoleId, CancellationToken cancellationToken);

    Task<Guid> CreateAsync(string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task UpdateAsync(Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task DeleteAsync(Guid roleId, CancellationToken cancellationToken);
}
