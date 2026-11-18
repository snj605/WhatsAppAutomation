using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Features.Contacts;
using WhatsAppAutomation.Domain.Entities;

namespace WhatsAppAutomation.Application.Features.Broadcasts;

/// <summary>One chosen contact and what they will get: a language and text, or the reason they are skipped.</summary>
internal sealed record PlannedRecipient(Contact Contact, string Name, ContactLanguage? Language, string? Text, string? SkipReason);

/// <summary>
/// Decides, for each chosen contact, which text they get and fills in their details. A message never goes out
/// with a wrong or missing name: such contacts are skipped with the reason.
/// </summary>
internal static partial class BroadcastPlanner
{
    /// <summary>The standard placeholders. The older single-brace forms "{name}" and "{salutation}" work too.</summary>
    public const string NamePlaceholder = "{{name}}";
    public const string SalutationPlaceholder = "{{salutation}}";

    /// <summary>True when the text asks for the person's name, in either placeholder form.</summary>
    public static bool UsesName(string text) => NameToken().IsMatch(text);

    public static async Task<List<PlannedRecipient>> PlanAsync(
        IAppDbContext db,
        Guid userId,
        BroadcastInput input,
        ContactLanguage defaultLanguage,
        int contactGapDays,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, string>? forNames = null)
    {
        var categoryIds = input.CategoryIds ?? [];
        var groupIds = input.GroupIds ?? [];
        var contactIds = input.ContactIds ?? [];
        var everyone = input.AllContacts;
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.UserId == userId &&
                (everyone || (c.CategoryId != null && categoryIds.Contains(c.CategoryId.Value)) || contactIds.Contains(c.Id) ||
                 c.Groups.Any(g => groupIds.Contains(g.Id))))
            .OrderBy(c => c.SavedName ?? c.NameEn ?? c.NameGu ?? c.NameHi ?? c.PhoneNumber)
            .ToListAsync(cancellationToken);

        var recentlySent = new HashSet<string>();
        if (contactGapDays > 0 && contacts.Count > 0)
        {
            var since = now.AddDays(-contactGapDays);
            var phones = contacts.Select(c => c.PhoneNumber).ToList();
            recentlySent = (await db.BroadcastRecipients.AsNoTracking()
                .Where(r => r.Status == RecipientStatus.Sent && r.ProcessedAt >= since && phones.Contains(r.PhoneNumber) &&
                    db.Broadcasts.Any(b => b.Id == r.BroadcastId && b.UserId == userId))
                .Select(r => r.PhoneNumber).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        }

        var fields = await FieldValues.LoadAsync(
            db, userId, [.. contacts.Select(c => c.Id)], [input.Text, input.TextEn, input.TextHi, input.TextGu], cancellationToken);
        if (IsSingleText(input))
        {
            var single = input.Text!.Trim();
            return contacts.Select(c => PlanSingle(
                c, single, recentlySent.Contains(c.PhoneNumber) ? contactGapDays : 0, forNames?.GetValueOrDefault(c.Id), fields)).ToList();
        }

        var texts = new Dictionary<ContactLanguage, string?>
        {
            [ContactLanguage.En] = Clean(input.TextEn),
            [ContactLanguage.Hi] = Clean(input.TextHi),
            [ContactLanguage.Gu] = Clean(input.TextGu),
        };

        return contacts.Select(c => Plan(c, texts, defaultLanguage, recentlySent.Contains(c.PhoneNumber) ? contactGapDays : 0, fields)).ToList();
    }

    /// <summary>
    /// Puts the person's details into a text. An empty salutation leaves no double spaces.
    /// Word choices such as "{Hi|Hello|Namaste}" are resolved first, one option picked at random for each
    /// person, so that recipients do not all get exactly the same text.
    /// </summary>
    public static string Render(string text, string? name, string? salutation) => Render(text, name, salutation, Random.Shared);

    internal static string Render(string text, string? name, string? salutation, Random random)
    {
        var chosen = Choices().Replace(text, m =>
        {
            var options = m.Groups[1].Value.Split('|');
            return options[random.Next(options.Length)].Trim();
        });
        var rendered = NameToken().Replace(chosen, name ?? string.Empty);
        rendered = SalutationToken().Replace(rendered, salutation ?? string.Empty);
        var lines = rendered.Split('\n').Select(line => DoubleSpaces().Replace(line, " ").Trim());
        return string.Join('\n', lines).Trim();
    }

    public static bool IsSingleText(BroadcastInput input) => !string.IsNullOrWhiteSpace(input.Text);

    /// <summary>
    /// Fills one message for one person. {{name}} is their English name, else how they are saved; {{name_en}},
    /// {{name_hi}} and {{name_gu}} are the name in that language; {{salutation}} may be empty; {{for_name}} is the
    /// name in the person's entry of an occasion module (for example who is remembered). Returns the text, or
    /// what is missing ("Hindi name") when the message needs a detail this person does not have.
    /// </summary>
    internal static (string? Text, string? Missing) RenderFor(
        string text, Contact c, Random random, string? forName = null, FieldValues? fields = null)
    {
        string? missing = null;
        var chosen = Choices().Replace(text, m =>
        {
            var options = m.Groups[1].Value.Split('|');
            return options[random.Next(options.Length)].Trim();
        });
        var rendered = LanguageNameToken().Replace(chosen, m =>
        {
            var code = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant();
            var language = ContactLanguages.Parse(code)!.Value;
            var value = c.NameIn(language);
            if (string.IsNullOrEmpty(value))
            {
                missing ??= $"{ContactLanguages.DisplayName(language)} name";
            }

            return value ?? string.Empty;
        });
        rendered = NameToken().Replace(rendered, m =>
        {
            var value = c.NameEn ?? c.SavedName;
            if (string.IsNullOrEmpty(value))
            {
                missing ??= "name";
            }

            return value ?? string.Empty;
        });
        rendered = SalutationToken().Replace(rendered, c.Salutation ?? string.Empty);
        rendered = ForNameToken().Replace(rendered, _ =>
        {
            if (string.IsNullOrEmpty(forName))
            {
                missing ??= "name for this occasion";
            }

            return forName ?? string.Empty;
        });
        rendered = (fields ?? FieldValues.None).Fill(rendered, c.Id, ref missing);
        if (missing is not null)
        {
            return (null, missing);
        }

        var lines = rendered.Split('\n').Select(line => DoubleSpaces().Replace(line, " ").Trim());
        return (string.Join('\n', lines).Trim(), null);
    }

    private static PlannedRecipient PlanSingle(Contact c, string text, int recentGapDays, string? forName, FieldValues fields)
    {
        var display = DisplayName(c);
        if (c.IsNotOnWhatsApp)
        {
            return new PlannedRecipient(c, display, null, null, WhatsAppChecks.NotOnWhatsAppReason);
        }

        if (c.OptedOutAt is not null)
        {
            return new PlannedRecipient(c, display, null, null, OptedOutReason);
        }

        if (recentGapDays > 0)
        {
            return new PlannedRecipient(c, display, null, null, $"Already got a broadcast in the last {recentGapDays} days.");
        }

        var (rendered, missing) = RenderFor(text, c, Random.Shared, forName, fields);
        if (missing is not null)
        {
            return new PlannedRecipient(c, display, null, null, $"Needs details: no {missing}.");
        }

        return rendered!.Length > Broadcast.TextMaxLength
            ? new PlannedRecipient(c, display, null, null, "The message is too long with this contact's details.")
            : new PlannedRecipient(c, display, null, rendered, null);
    }

    public static string DisplayName(Contact c) => c.SavedName ?? c.NameIn(c.Language) ?? c.NameEn ?? c.NameGu ?? c.NameHi ?? c.PhoneNumber;

    private static PlannedRecipient Plan(
        Contact c, Dictionary<ContactLanguage, string?> texts, ContactLanguage defaultLanguage, int recentGapDays, FieldValues fields)
    {
        var display = DisplayName(c);
        if (c.IsNotOnWhatsApp)
        {
            return new PlannedRecipient(c, display, null, null, WhatsAppChecks.NotOnWhatsAppReason);
        }

        if (c.OptedOutAt is not null)
        {
            return new PlannedRecipient(c, display, null, null, OptedOutReason);
        }

        if (recentGapDays > 0)
        {
            return new PlannedRecipient(c, display, null, null, $"Already got a broadcast in the last {recentGapDays} days.");
        }

        // The contact's own language, else the user's default language (blueprint section 1).
        var language = texts[c.Language] is not null ? c.Language : texts[defaultLanguage] is not null ? defaultLanguage : (ContactLanguage?)null;
        if (language is not { } lang)
        {
            var reason = c.Language == defaultLanguage
                ? $"No text in {ContactLanguages.DisplayName(c.Language)}, their language."
                : $"No text in {ContactLanguages.DisplayName(c.Language)} (their language) or {ContactLanguages.DisplayName(defaultLanguage)} (your default).";
            return new PlannedRecipient(c, display, null, null, reason);
        }

        var text = texts[lang]!;
        var name = c.NameIn(lang);
        if (string.IsNullOrEmpty(name) && UsesName(text))
        {
            return new PlannedRecipient(c, display, lang, null, $"Needs details: no {ContactLanguages.DisplayName(lang)} name.");
        }

        var rendered = ForNameToken().Replace(Render(text, name, c.Salutation), string.Empty);
        string? missingField = null;
        rendered = fields.Fill(rendered, c.Id, ref missingField);
        if (missingField is not null)
        {
            return new PlannedRecipient(c, display, lang, null, $"Needs details: no {missingField}.");
        }

        return rendered.Length > Broadcast.TextMaxLength
            ? new PlannedRecipient(c, display, lang, null, "The message is too long with this contact's details.")
            : new PlannedRecipient(c, display, lang, rendered, null);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public const string OptedOutReason = "Marked as not wanting broadcasts.";

    [GeneratedRegex(" {2,}")]
    private static partial Regex DoubleSpaces();

    /// <summary>"{{name}}" or "{name}", any letter case, spaces allowed inside the double braces.</summary>
    [GeneratedRegex(@"\{\{\s*name\s*\}\}|\{name\}", RegexOptions.IgnoreCase)]
    private static partial Regex NameToken();

    /// <summary>"{{name_hi}}" or "{name_hi}" (also _en, _gu), any letter case.</summary>
    [GeneratedRegex(@"\{\{\s*name_(en|hi|gu)\s*\}\}|\{name_(en|hi|gu)\}", RegexOptions.IgnoreCase)]
    private static partial Regex LanguageNameToken();

    [GeneratedRegex(@"\{\{\s*salutation\s*\}\}|\{salutation\}", RegexOptions.IgnoreCase)]
    private static partial Regex SalutationToken();

    /// <summary>"{{for_name}}" or "{for_name}", any letter case.</summary>
    [GeneratedRegex(@"\{\{\s*for_name\s*\}\}|\{for_name\}", RegexOptions.IgnoreCase)]
    private static partial Regex ForNameToken();

    /// <summary>"{a|b|c}": at least one "|" and no braces inside, so it never matches a placeholder.</summary>
    [GeneratedRegex(@"\{([^{}|]*(?:\|[^{}|]*)+)\}")]
    private static partial Regex Choices();
}
