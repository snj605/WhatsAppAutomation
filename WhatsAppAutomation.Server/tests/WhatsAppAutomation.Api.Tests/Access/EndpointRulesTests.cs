using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using WhatsAppAutomation.Api.Authorization;

namespace WhatsAppAutomation.Api.Tests.Access;

/// <summary>Every endpoint says who may call it: a permission, signed in, the gateway secret, or open on purpose.</summary>
public sealed class EndpointRulesTests
{
    [Fact]
    public void Every_endpoint_has_an_access_rule()
    {
        var missing = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Select(m => (t, m)))
            .Where(x => x.m.GetCustomAttributes<HttpMethodAttribute>().Any())
            .Where(x => !HasRule(x.m) && !HasRule(x.t))
            .Select(x => $"{x.t.Name}.{x.m.Name}")
            .ToList();

        Assert.Empty(missing);
    }

    private static bool HasRule(MemberInfo member) =>
        member.GetCustomAttributes<AuthorizeAttribute>(true).Any() ||
        member.GetCustomAttributes<AllowAnonymousAttribute>(true).Any() ||
        member.GetCustomAttributes(true).Any(a => a.GetType().Name is nameof(HasPermissionAttribute) or "GatewaySecretAttribute");
}
