using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;

namespace WhatsAppAutomation.Api.Authorization;

/// <summary>
/// Put on a controller action to require a permission, e.g. [HasPermission(Permissions.Users.View)].
/// It is a normal [Authorize] with a policy name that our policy provider understands.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class HasPermissionAttribute(string permission)
    : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission);

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Builds the policy for "permission:xyz" names on the fly, so there is no need to register one
/// policy per permission. Any other policy name goes to the default provider.
/// </summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "permission:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
    }
}

/// <summary>Allows the request when the signed-in user's roles grant the permission.</summary>
internal sealed class PermissionAuthorizationHandler(IPermissionService permissions)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId))
        {
            return;
        }

        var granted = await permissions.GetPermissionsAsync(userId, CancellationToken.None);
        if (granted.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst("sub")?.Value, out var id) ? id : null;
}

internal static class AuthorizationSetup
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}
