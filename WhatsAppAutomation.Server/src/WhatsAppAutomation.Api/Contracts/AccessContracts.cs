using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Api.Contracts;

public sealed record CreatedResponse(Guid Id);

/// <summary>A person of the tenant as the members screen shows them.</summary>
/// <param name="Kind">"tenant_admin" or "member".</param>
/// <param name="Switches">For members: the switches that are on. Empty for admins, who may do everything.</param>
/// <param name="WhatsAppStatus">The state of their WhatsApp link, like GET /api/v1/whatsapp.</param>
public sealed record MemberResponse(
    Guid Id,
    string UserName,
    string DisplayName,
    string Kind,
    bool IsActive,
    IReadOnlyList<string> Switches,
    WhatsAppConnectionStatus WhatsAppStatus,
    string? WhatsAppNumber,
    DateTimeOffset CreatedAt);

public sealed record CreateMemberRequest(
    string UserName, string DisplayName, string Password, bool IsAdmin, IReadOnlyList<string>? Switches);

public sealed record UpdateMemberRequest(string DisplayName, bool IsAdmin, IReadOnlyList<string>? Switches);

public sealed record SetActiveRequest(bool IsActive);

public sealed record SetPasswordRequest(string Password);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>A tenant as the platform admin sees it: counts only, nothing of what is inside.</summary>
public sealed record TenantResponse(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, int Users, int Admins);

public sealed record CreateTenantRequest(string Name, string AdminUserName, string AdminDisplayName, string AdminPassword);

public sealed record RenameTenantRequest(string Name);

/// <summary>A tenant admin as the platform admin sees them.</summary>
public sealed record TenantAdminResponse(Guid Id, string UserName, string DisplayName, bool IsActive, DateTimeOffset CreatedAt);

public sealed record CreateTenantAdminRequest(string UserName, string DisplayName, string Password);

public sealed record UpdateTenantAdminRequest(string DisplayName);
