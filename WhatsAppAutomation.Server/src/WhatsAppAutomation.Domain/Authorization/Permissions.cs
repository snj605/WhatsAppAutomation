namespace WhatsAppAutomation.Domain.Authorization;

/// <summary>One thing a user may be allowed to do, e.g. "broadcasts.send".</summary>
/// <param name="Name">Stable id stored in the database and checked by the API. Never rename one.</param>
/// <param name="Group">Heading the screens group it under.</param>
/// <param name="Description">What it allows, in plain words.</param>
public sealed record PermissionDefinition(string Name, string Group, string Description);

/// <summary>
/// Every permission in the system. Permissions live in code because each one guards code: an endpoint
/// checks a permission. Who has which follows from the kind of user (<see cref="UserKinds"/>): the platform
/// admin manages tenants, a tenant admin may do everything inside the tenant, and a member has the base
/// permissions plus the switches the tenant admin turned on.
/// </summary>
public static class Permissions
{
    public const int NameMaxLength = 100;

    public static class Platform
    {
        /// <summary>Create and manage tenants and their admins. Never reaches any tenant's contacts or messages.</summary>
        public const string Tenants = "platform.tenants";
    }

    public static class Members
    {
        public const string Manage = "members.manage";
    }

    public static class WhatsApp
    {
        public const string Link = "whatsapp.link";
    }

    public static class Messages
    {
        public const string Send = "messages.send";
    }

    public static class Contacts
    {
        public const string View = "contacts.view";
        public const string Manage = "contacts.manage";
        public const string Import = "contacts.import";
    }

    public static class Broadcasts
    {
        public const string Send = "broadcasts.send";
    }

    public static class Templates
    {
        public const string Manage = "templates.manage";
    }

    public static class Modules
    {
        public const string Manage = "modules.manage";
    }

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Platform.Tenants, "Platform", "Create tenants and their admins, turn them on or off."),
        new(Members.Manage, "Members", "Add members to the tenant, turn their switches on or off, remove them."),
        new(WhatsApp.Link, "WhatsApp", "Link and unlink your own WhatsApp number."),
        new(Messages.Send, "Messages", "Send messages from your linked WhatsApp number."),
        new(Contacts.View, "Contacts", "See your own contacts and groups."),
        new(Contacts.Manage, "Contacts", "Add, edit and delete your own contacts and groups."),
        new(Contacts.Import, "Contacts", "Import many contacts at once from Excel or CSV."),
        new(Broadcasts.Send, "Broadcasts", "Send to your own groups and lists, pause and stop them, see their results."),
        new(Templates.Manage, "Templates", "Make, change and delete message templates."),
        new(Modules.Manage, "Modules", "Make your own occasion modules (like Birthday) and send their wishes."),
    ];

    private static readonly HashSet<string> Names = [.. All.Select(p => p.Name)];

    public static bool IsKnown(string name) => Names.Contains(name);
}
