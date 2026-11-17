namespace WhatsAppAutomation.Domain.Authorization;

/// <summary>
/// Roles the system creates itself. The Owner role always has every permission and cannot be
/// edited or deleted, so there is always someone who can manage the system. Sender is a normal
/// role created at install time; the Owner can change or delete it like any role they create.
/// </summary>
public static class SystemRoles
{
    public const string Owner = "Owner";
    public const string Sender = "Sender";

    public const int NameMinLength = 2;
    public const int NameMaxLength = 50;
    public const int DescriptionMaxLength = 200;
}
