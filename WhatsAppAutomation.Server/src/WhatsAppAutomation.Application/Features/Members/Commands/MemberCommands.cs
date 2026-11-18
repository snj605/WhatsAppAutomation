using FluentValidation;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Messaging;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.Authentication;
using WhatsAppAutomation.Application.Features.Tenants;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Members.Commands;

internal static class MemberRules
{
    public static void UserName<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(50)
            .Matches("^[a-zA-Z0-9._-]+$").WithMessage("User name may only use letters, digits, '.', '_' and '-'.");

    public static void DisplayName<T>(IRuleBuilder<T, string> rule) => rule.NotEmpty().MaximumLength(100);

    public static void Switches<T>(IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotNull().Must(s => s.Count <= UserKinds.MemberSwitches.Count && s.All(UserKinds.MemberSwitches.Contains))
            .WithMessage("One of the chosen switches does not exist.");
}

/// <summary>
/// Adds a person with a first password chosen by the admin; they must choose their own at the first sign-in.
/// A tenant admin adds members or other admins to their own tenant (TenantId is ignored). The platform admin
/// adds tenant admins to the tenant in TenantId.
/// </summary>
public sealed record CreateMemberCommand(
    string UserName, string DisplayName, string Password, bool IsAdmin, IReadOnlyList<string> Switches, Guid? TenantId = null)
    : ICommand<Guid>;

internal sealed class CreateMemberCommandValidator : AbstractValidator<CreateMemberCommand>
{
    public CreateMemberCommandValidator()
    {
        MemberRules.UserName(RuleFor(c => c.UserName));
        MemberRules.DisplayName(RuleFor(c => c.DisplayName));
        RuleFor(c => c.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
        MemberRules.Switches(RuleFor(c => c.Switches));
    }
}

internal sealed class CreateMemberCommandHandler(
    MemberAccess access, IIdentityService identity, IAppDbContext db, Platform.AuditLog audit, TimeProvider clock) : ICommandHandler<CreateMemberCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateMemberCommand command, CancellationToken cancellationToken)
    {
        if (await access.CallerAsync(cancellationToken) is not { } caller)
        {
            return MemberErrors.NotFound;
        }

        Guid tenantId;
        UserKind kind;
        if (caller is { Kind: UserKind.TenantAdmin, TenantId: { } own })
        {
            tenantId = own;
            kind = command.IsAdmin ? UserKind.TenantAdmin : UserKind.Member;
        }
        else if (caller.Kind == UserKind.PlatformAdmin && command.TenantId is { } chosen &&
                 await db.Tenants.AnyAsync(t => t.Id == chosen, cancellationToken))
        {
            tenantId = chosen;
            kind = UserKind.TenantAdmin;
        }
        else
        {
            return TenantErrors.NotFound;
        }

        var userName = command.UserName.Trim();
        if (PasswordRules.Problem(command.Password, userName, kind != UserKind.Member) is { } problem)
        {
            return Error.Validation(problem);
        }

        if (await identity.UserNameExistsAsync(userName, cancellationToken))
        {
            return MemberErrors.UserNameTaken;
        }

        var created = await identity.CreateUserAsync(
            new NewUser(userName, command.DisplayName.Trim(), command.Password, kind, tenantId,
                kind == UserKind.Member ? command.Switches : [], MustChangePassword: true),
            cancellationToken);

        // Tenant admins start with a Birthday occasion; members make their own.
        if (created.IsSuccess && kind == UserKind.TenantAdmin)
        {
            await Modules.DefaultModules.AddBirthdayAsync(db, created.Value, clock.GetUtcNow(), cancellationToken);
        }

        if (created.IsSuccess)
        {
            await audit.WriteAsync("member.created",
                $"{caller.DisplayName} added {command.DisplayName.Trim()} ({(kind == UserKind.TenantAdmin ? "admin" : "member")})", tenantId, cancellationToken);
        }

        return created;
    }
}

/// <summary>Changes a person's name, whether they are an admin, and (for members) their switches.</summary>
public sealed record UpdateMemberCommand(Guid UserId, string DisplayName, bool IsAdmin, IReadOnlyList<string> Switches) : ICommand;

internal sealed class UpdateMemberCommandValidator : AbstractValidator<UpdateMemberCommand>
{
    public UpdateMemberCommandValidator()
    {
        MemberRules.DisplayName(RuleFor(c => c.DisplayName));
        MemberRules.Switches(RuleFor(c => c.Switches));
    }
}

internal sealed class UpdateMemberCommandHandler(
    MemberAccess access, IIdentityService identity, IPermissionService permissions, Platform.AuditLog audit)
    : ICommandHandler<UpdateMemberCommand>
{
    public async Task<Result> Handle(UpdateMemberCommand command, CancellationToken cancellationToken)
    {
        var found = await access.TargetAsync(command.UserId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var (caller, target) = found.Value;

        // The platform admin manages tenant admins only, so it cannot turn one into a member.
        var kind = caller.Kind == UserKind.PlatformAdmin || command.IsAdmin ? UserKind.TenantAdmin : UserKind.Member;
        if (kind == UserKind.Member && await access.IsLastAdminAsync(target, cancellationToken))
        {
            return MemberErrors.LastAdmin;
        }

        await identity.UpdateUserAsync(target.Id, command.DisplayName.Trim(), kind, command.Switches, cancellationToken);
        permissions.InvalidateAll();
        var what = kind == UserKind.TenantAdmin ? "admin" : command.Switches.Count == 0 ? "no switches" : string.Join(", ", command.Switches);
        await audit.WriteAsync("member.changed", $"{caller.DisplayName} set {target.DisplayName}: {what}", target.TenantId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>Turns a person on or off. Turning off signs them out on every device; nothing is deleted.</summary>
public sealed record SetMemberActiveCommand(Guid UserId, bool IsActive) : ICommand;

internal sealed class SetMemberActiveCommandHandler(
    MemberAccess access, IIdentityService identity, SessionStore sessions, IPermissionService permissions, Platform.AuditLog audit, TimeProvider clock)
    : ICommandHandler<SetMemberActiveCommand>
{
    public async Task<Result> Handle(SetMemberActiveCommand command, CancellationToken cancellationToken)
    {
        var found = await access.TargetAsync(command.UserId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var target = found.Value.Target;

        if (!command.IsActive && await access.IsLastAdminAsync(target, cancellationToken))
        {
            return MemberErrors.LastAdmin;
        }

        await identity.SetActiveAsync(target.Id, command.IsActive, cancellationToken);
        if (!command.IsActive)
        {
            await sessions.RevokeUserAsync(target.Id, clock.GetUtcNow(), cancellationToken);
        }

        permissions.InvalidateAll();
        await audit.WriteAsync(command.IsActive ? "member.turned_on" : "member.turned_off",
            $"{target.DisplayName} turned {(command.IsActive ? "on" : "off")}", target.TenantId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Sets a new password for someone who forgot theirs. They are signed out everywhere and must choose their
/// own password at the next sign-in.
/// </summary>
public sealed record ResetMemberPasswordCommand(Guid UserId, string Password) : ICommand;

internal sealed class ResetMemberPasswordCommandValidator : AbstractValidator<ResetMemberPasswordCommand>
{
    public ResetMemberPasswordCommandValidator() =>
        RuleFor(c => c.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
}

internal sealed class ResetMemberPasswordCommandHandler(
    MemberAccess access, IIdentityService identity, SessionStore sessions, IPermissionService permissions, Platform.AuditLog audit, TimeProvider clock)
    : ICommandHandler<ResetMemberPasswordCommand>
{
    public async Task<Result> Handle(ResetMemberPasswordCommand command, CancellationToken cancellationToken)
    {
        var found = await access.TargetAsync(command.UserId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var target = found.Value.Target;

        if (PasswordRules.Problem(command.Password, target.UserName, target.Kind != UserKind.Member) is { } problem)
        {
            return Error.Validation(problem);
        }

        var reset = await identity.ResetPasswordAsync(target.Id, command.Password, cancellationToken);
        if (!reset.IsSuccess)
        {
            return reset;
        }

        await sessions.RevokeUserAsync(target.Id, clock.GetUtcNow(), cancellationToken);
        permissions.InvalidateAll();
        await audit.WriteAsync("member.password_reset", $"New password set for {target.DisplayName}", target.TenantId, cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Deletes a person for good, with everything that is theirs: contacts, groups, templates, messages and their
/// linked WhatsApp number (logged out first). Cannot be undone; turning them off is the reversible choice.
/// </summary>
public sealed record DeleteMemberCommand(Guid UserId) : ICommand;

internal sealed class DeleteMemberCommandHandler(
    MemberAccess access, IIdentityService identity, UserRemoval removal, IPermissionService permissions, Platform.AuditLog audit)
    : ICommandHandler<DeleteMemberCommand>
{
    public async Task<Result> Handle(DeleteMemberCommand command, CancellationToken cancellationToken)
    {
        var found = await access.TargetAsync(command.UserId, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var target = found.Value.Target;

        if (await access.IsLastAdminAsync(target, cancellationToken))
        {
            return MemberErrors.LastAdmin;
        }

        await removal.UnlinkNumbersAsync([target.Id], cancellationToken);
        await identity.DeleteUserAsync(target.Id, cancellationToken);
        permissions.InvalidateAll();
        await audit.WriteAsync("member.deleted", $"{target.DisplayName} deleted", target.TenantId, cancellationToken);
        return Result.Success();
    }
}
