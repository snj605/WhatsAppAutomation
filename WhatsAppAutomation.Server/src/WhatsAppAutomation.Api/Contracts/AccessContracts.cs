namespace WhatsAppAutomation.Api.Contracts;

public sealed record PermissionResponse(string Name, string Group, string Description);

public sealed record RoleResponse(
    Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, int UserCount);

public sealed record SaveRoleRequest(string Name, string? Description, IReadOnlyList<string> Permissions);

public sealed record CreatedResponse(Guid Id);

public sealed record RoleRefResponse(Guid Id, string Name);

public sealed record UserDetailsResponse(
    Guid Id, string UserName, string DisplayName, bool IsActive, IReadOnlyList<RoleRefResponse> Roles, DateTimeOffset CreatedAt);

public sealed record CreateUserRequest(string UserName, string DisplayName, string Password, IReadOnlyList<Guid> RoleIds);

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

public sealed record SetUserActiveRequest(bool IsActive);
