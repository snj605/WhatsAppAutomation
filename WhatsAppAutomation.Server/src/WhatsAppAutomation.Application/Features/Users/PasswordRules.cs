using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Users;

/// <summary>
/// What a new password must satisfy. Ordinary users get a short, easy rule: the lock after five wrong
/// tries is what protects a short password, and on the phone they mostly unlock with a fingerprint.
/// Accounts that can manage users or roles keep the long rule, because one such password reaches every account.
/// </summary>
public static class PasswordRules
{
    public const int UserMinLength = 6;

    public const int AdminMinLength = 12;

    private static readonly HashSet<string> Obvious = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "passwords", "qwerty", "qwertyui", "abc123", "iloveyou", "welcome", "whatsapp", "admin123", "india123",
    };

    /// <summary>Whether any of these roles lets its holder manage users or roles.</summary>
    public static bool ManagesAccounts(IEnumerable<RoleDetails> roles) =>
        roles.Any(r => r.Permissions.Contains(Permissions.Users.Manage) || r.Permissions.Contains(Permissions.Roles.Manage));

    /// <summary>Why this password is not accepted, in plain words, or null when it is fine.</summary>
    public static string? Problem(string password, string userName, bool managesAccounts)
    {
        var minLength = managesAccounts ? AdminMinLength : UserMinLength;
        if (password.Length < minLength)
        {
            return managesAccounts
                ? $"The password needs at least {AdminMinLength} characters, because this account can manage users or roles."
                : $"The password needs at least {UserMinLength} characters.";
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
