namespace WhatsAppAutomation.Domain.Authorization;

/// <summary>The three kinds of user. Stored as "platform_admin", "tenant_admin" or "member".</summary>
public enum UserKind
{
    /// <summary>Runs the platform: tenants and their admins. Has no WhatsApp number, contacts or messages.</summary>
    PlatformAdmin,

    /// <summary>Runs one tenant: may do everything inside it and manages its members.</summary>
    TenantAdmin,

    /// <summary>A person in a tenant: the base permissions plus the switches the tenant admin turned on.</summary>
    Member,
}

/// <summary>What each kind of user may do.</summary>
public static class UserKinds
{
    public const string PlatformAdminCode = "platform_admin";
    public const string TenantAdminCode = "tenant_admin";
    public const string MemberCode = "member";

    public static string ToCode(UserKind kind) => kind switch
    {
        UserKind.PlatformAdmin => PlatformAdminCode,
        UserKind.TenantAdmin => TenantAdminCode,
        _ => MemberCode,
    };

    public static UserKind Parse(string code) => code switch
    {
        PlatformAdminCode => UserKind.PlatformAdmin,
        TenantAdminCode => UserKind.TenantAdmin,
        _ => UserKind.Member,
    };

    /// <summary>The platform admin manages tenants only.</summary>
    public static IReadOnlySet<string> PlatformAdmin { get; } = new HashSet<string> { Permissions.Platform.Tenants };

    /// <summary>A tenant admin may do everything inside the tenant.</summary>
    public static IReadOnlySet<string> TenantAdmin { get; } =
        Permissions.All.Select(p => p.Name).Where(p => p != Permissions.Platform.Tenants).ToHashSet();

    /// <summary>Every member may link their number, send single messages and keep their own contacts.</summary>
    public static IReadOnlySet<string> MemberBase { get; } = new HashSet<string>
    {
        Permissions.WhatsApp.Link,
        Permissions.Messages.Send,
        Permissions.Contacts.View,
        Permissions.Contacts.Manage,
    };

    /// <summary>The switches a tenant admin can turn on for a member, in the order the screens show them.</summary>
    public static IReadOnlyList<string> MemberSwitches { get; } =
    [
        Permissions.Modules.Manage,
        Permissions.Broadcasts.Send,
        Permissions.Contacts.Import,
        Permissions.Templates.Manage,
    ];

    /// <summary>The permissions of a user of this kind with these switches on (switches only count for members).</summary>
    public static IReadOnlySet<string> For(UserKind kind, IEnumerable<string> switches) => kind switch
    {
        UserKind.PlatformAdmin => PlatformAdmin,
        UserKind.TenantAdmin => TenantAdmin,
        _ => MemberBase.Concat(switches.Where(MemberSwitches.Contains)).ToHashSet(),
    };
}
