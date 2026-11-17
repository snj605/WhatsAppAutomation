using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Infrastructure.Identity;

internal sealed class RoleService(AppDbContext db, ILookupNormalizer normalizer) : IRoleService
{
    public async Task<IReadOnlyList<RoleDetails>> ListAsync(CancellationToken cancellationToken) =>
        await QueryAsync(db.Roles, cancellationToken);

    public async Task<RoleDetails?> FindAsync(Guid roleId, CancellationToken cancellationToken) =>
        (await QueryAsync(db.Roles.Where(r => r.Id == roleId), cancellationToken)).SingleOrDefault();

    public async Task<IReadOnlyList<RoleDetails>> FindManyAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken) =>
        await QueryAsync(db.Roles.Where(r => roleIds.Contains(r.Id)), cancellationToken);

    public Task<bool> NameExistsAsync(string name, Guid? exceptRoleId, CancellationToken cancellationToken)
    {
        var normalized = normalizer.NormalizeName(name);
        return db.Roles.AnyAsync(r => r.NormalizedName == normalized && r.Id != exceptRoleId, cancellationToken);
    }

    public async Task<Guid> CreateAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = new AppRole
        {
            Id = Guid.NewGuid(),
            Name = name,
            NormalizedName = normalizer.NormalizeName(name),
            Description = description,
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };

        db.Roles.Add(role);
        db.RolePermissions.AddRange(permissions.Select(p => new RolePermission(role.Id, p)));
        await db.SaveChangesAsync(cancellationToken);
        return role.Id;
    }

    public async Task UpdateAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = await db.Roles.SingleAsync(r => r.Id == roleId, cancellationToken);
        role.Name = name;
        role.NormalizedName = normalizer.NormalizeName(name);
        role.Description = description;
        role.ConcurrencyStamp = Guid.NewGuid().ToString();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.RolePermissions.Where(rp => rp.RoleId == roleId).ExecuteDeleteAsync(cancellationToken);
        db.RolePermissions.AddRange(permissions.Select(p => new RolePermission(roleId, p)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public Task DeleteAsync(Guid roleId, CancellationToken cancellationToken) =>
        db.Roles.Where(r => r.Id == roleId).ExecuteDeleteAsync(cancellationToken);

    private async Task<IReadOnlyList<RoleDetails>> QueryAsync(IQueryable<AppRole> roles, CancellationToken cancellationToken)
    {
        // One SQL statement: EF turns the nested list and the count into sub-queries.
        var rows = await roles.AsNoTracking()
            .OrderByDescending(r => r.IsSystem).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Description,
                r.IsSystem,
                Permissions = db.RolePermissions.Where(rp => rp.RoleId == r.Id).Select(rp => rp.Permission).OrderBy(p => p).ToList(),
                UserCount = db.UserRoles.Count(ur => ur.RoleId == r.Id),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new RoleDetails(
            r.Id,
            r.Name!,
            r.Description,
            r.IsSystem,
            r.IsSystem ? AllPermissionNames : r.Permissions,
            r.UserCount))];
    }

    private static readonly IReadOnlyList<string> AllPermissionNames = [.. Permissions.All.Select(p => p.Name)];
}
