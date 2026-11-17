using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Members;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Tenants.Commands;

/// <summary>Creates a tenant together with its first admin, who must choose their own password at the first sign-in.</summary>
public sealed record CreateTenantCommand(string Name, string AdminUserName, string AdminDisplayName, string AdminPassword)
    : ICommand<Guid>;

internal sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
        RuleFor(c => c.AdminUserName).NotEmpty().MaximumLength(50)
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("User name may only use letters, digits, '.', '_' and '-'.");
        RuleFor(c => c.AdminDisplayName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.AdminPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

internal sealed class CreateTenantCommandHandler(IAppDbContext db, IIdentityService identity, TimeProvider clock)
    : ICommandHandler<CreateTenantCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var name = command.Name.Trim();
        var userName = command.AdminUserName.Trim();
        if (await NameTakenAsync(db, name, null, cancellationToken))
        {
            return TenantErrors.NameTaken;
        }

        if (PasswordRules.Problem(command.AdminPassword, userName, isAdmin: true) is { } problem)
        {
            return Error.Validation(problem);
        }

        if (await identity.UserNameExistsAsync(userName, cancellationToken))
        {
            return MemberErrors.UserNameTaken;
        }

        var tenant = new Tenant(name, clock.GetUtcNow());
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);

        var admin = await identity.CreateUserAsync(
            new NewUser(userName, command.AdminDisplayName.Trim(), command.AdminPassword, UserKind.TenantAdmin, tenant.Id, [],
                MustChangePassword: true),
            cancellationToken);
        if (!admin.IsSuccess)
        {
            // Everything was checked above, so this is rare (e.g. the same user name taken a moment ago).
            // Take the empty tenant away again so no tenant is left without an admin.
            await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteDeleteAsync(cancellationToken);
            return admin.Error!;
        }

        return tenant.Id;
    }

    internal static Task<bool> NameTakenAsync(IAppDbContext db, string name, Guid? except, CancellationToken cancellationToken) =>
        db.Tenants.AnyAsync(t => t.Name.ToLower() == name.ToLower() && t.Id != except, cancellationToken);
}

public sealed record RenameTenantCommand(Guid TenantId, string Name) : ICommand;

internal sealed class RenameTenantCommandValidator : AbstractValidator<RenameTenantCommand>
{
    public RenameTenantCommandValidator() => RuleFor(c => c.Name).NotEmpty().MaximumLength(Tenant.NameMaxLength);
}

internal sealed class RenameTenantCommandHandler(IAppDbContext db) : ICommandHandler<RenameTenantCommand>
{
    public async Task<Result> Handle(RenameTenantCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        if (await CreateTenantCommandHandler.NameTakenAsync(db, command.Name.Trim(), tenant.Id, cancellationToken))
        {
            return TenantErrors.NameTaken;
        }

        tenant.Rename(command.Name);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Turns a tenant on or off. While off nobody in it can sign in (their sessions end) and nothing is sent for
/// it; queued messages wait and go out once it is turned on again. Nothing is deleted.
/// </summary>
public sealed record SetTenantActiveCommand(Guid TenantId, bool IsActive) : ICommand;

internal sealed class SetTenantActiveCommandHandler(
    IAppDbContext db, IIdentityService identity, SessionStore sessions, IPermissionService permissions, TimeProvider clock)
    : ICommandHandler<SetTenantActiveCommand>
{
    public async Task<Result> Handle(SetTenantActiveCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        tenant.SetActive(command.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        if (!command.IsActive)
        {
            var now = clock.GetUtcNow();
            foreach (var user in await identity.ListUsersAsync(tenant.Id, cancellationToken))
            {
                await sessions.RevokeUserAsync(user.Id, now, cancellationToken);
            }
        }

        permissions.InvalidateAll();
        return Result.Success();
    }
}

/// <summary>
/// Deletes a turned-off tenant for good, with all of its people and everything that is theirs. Their linked
/// WhatsApp numbers are logged out first. Cannot be undone.
/// </summary>
public sealed record DeleteTenantCommand(Guid TenantId) : ICommand;

internal sealed class DeleteTenantCommandHandler(
    IAppDbContext db, IIdentityService identity, UserRemoval removal, IPermissionService permissions)
    : ICommandHandler<DeleteTenantCommand>
{
    public async Task<Result> Handle(DeleteTenantCommand command, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == command.TenantId, cancellationToken);
        if (tenant is null)
        {
            return TenantErrors.NotFound;
        }

        if (tenant.IsActive)
        {
            return TenantErrors.StillActive;
        }

        var users = await identity.ListUsersAsync(tenant.Id, cancellationToken);
        await removal.UnlinkNumbersAsync([.. users.Select(u => u.Id)], cancellationToken);

        // The users go with the tenant (cascade), and everything of theirs with them.
        await db.Tenants.Where(t => t.Id == tenant.Id).ExecuteDeleteAsync(cancellationToken);
        permissions.InvalidateAll();
        return Result.Success();
    }
}
