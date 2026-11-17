namespace WhatsAppAutomation.Domain.Authorization;

/// <summary>One thing a user may be allowed to do, e.g. "users.view".</summary>
/// <param name="Name">Stable id stored in the database and checked by the API. Never rename one.</param>
/// <param name="Group">Heading the settings screen groups it under.</param>
/// <param name="Description">What it allows, in plain words.</param>
public sealed record PermissionDefinition(string Name, string Group, string Description);

/// <summary>
/// Every permission in the system. Permissions live in code because each one guards code: an
/// endpoint checks a permission, so a permission without code would do nothing. The Owner decides
/// which role gets which permission; the list itself only changes when the code changes.
/// Adding one here and running "dotnet ef migrations add" writes it to the permissions table.
/// </summary>
public static class Permissions
{
    public const int NameMaxLength = 100;

    public static class Users
    {
        public const string View = "users.view";
        public const string Manage = "users.manage";
    }

    public static class Roles
    {
        public const string View = "roles.view";
        public const string Manage = "roles.manage";
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
    }

    public static class Broadcasts
    {
        public const string Send = "broadcasts.send";
    }

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Users.View, "Users", "See the list of users and their roles."),
        new(Users.Manage, "Users", "Create users, change their roles, and enable or disable them."),
        new(Roles.View, "Roles", "See roles and the permissions each one has."),
        new(Roles.Manage, "Roles", "Create, edit and delete roles and choose their permissions."),
        new(WhatsApp.Link, "WhatsApp", "Link and unlink your own WhatsApp number."),
        new(Messages.Send, "Messages", "Send messages from your linked WhatsApp number."),
        new(Contacts.View, "Contacts", "See your own contacts, categories and groups."),
        new(Contacts.Manage, "Contacts", "Add, edit, import and delete your own contacts, categories and groups."),
        new(Broadcasts.Send, "Broadcasts", "Create, start, pause and cancel your own broadcasts, and see their progress."),
    ];

    private static readonly HashSet<string> Names = [.. All.Select(p => p.Name)];

    public static bool IsKnown(string name) => Names.Contains(name);
}
