namespace WhatsAppAutomation.Application.Features.Members;

/// <summary>
/// What a new password must satisfy. Members get a short, easy rule: the lock after five wrong tries is what
/// protects a short password, and on the phone they mostly unlock with a fingerprint. Admins (platform and
/// tenant) need the long rule, because one admin password reaches many accounts.
/// </summary>
public static class PasswordRules
{
    public const int MemberMinLength = 6;

    public const int AdminMinLength = 12;

    public const int MaxLength = 128;

    private static readonly HashSet<string> Obvious = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwords", "qwerty", "qwertyui", "abc123", "iloveyou", "welcome", "whatsapp", "admin123", "india123",
    };

    /// <summary>Why this password is not accepted, in plain words, or null when it is fine.</summary>
    public static string? Problem(string password, string userName, bool isAdmin)
    {
        var minLength = isAdmin ? AdminMinLength : MemberMinLength;
        if (password.Length < minLength)
        {
            return isAdmin
                ? $"The password needs at least {AdminMinLength} characters, because this is an admin account."
                : $"The password needs at least {MemberMinLength} characters.";
        }

        if (password.Length > MaxLength)
        {
            return $"The password can have at most {MaxLength} characters.";
        }

        return IsObvious(password, userName)
            ? "This password is too easy to guess. Avoid the user name, one repeated character, and runs like 123456."
            : null;
    }

    internal static bool IsObvious(string password, string userName) =>
        password.Distinct().Count() == 1 ||
        IsRun(password, 1) ||
        IsRun(password, -1) ||
        string.Equals(password, userName.Trim(), StringComparison.OrdinalIgnoreCase) ||
        Obvious.Contains(password);

    /// <summary>Every character follows the one before by the same step: "123456", "abcdef", "654321".</summary>
    private static bool IsRun(string text, int step)
    {
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] - text[i - 1] != step)
            {
                return false;
            }
        }

        return true;
    }
}
