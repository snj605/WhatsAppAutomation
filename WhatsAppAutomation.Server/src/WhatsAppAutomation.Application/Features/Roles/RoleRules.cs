using FluentValidation;
using WhatsAppAutomation.Domain.Authorization;

namespace WhatsAppAutomation.Application.Features.Roles;

internal static class RoleRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, string> name, Func<T, string?> description, Func<T, IReadOnlyList<string>> permissions)
    {
        validator.RuleFor(c => name(c)).NotEmpty()
            .Length(SystemRoles.NameMinLength, SystemRoles.NameMaxLength)
            .Must(n => !string.Equals(n?.Trim(), SystemRoles.Owner, StringComparison.OrdinalIgnoreCase))
            .WithMessage($"The name '{SystemRoles.Owner}' is reserved.")
            .OverridePropertyName("Name");
        validator.RuleFor(c => description(c)).MaximumLength(SystemRoles.DescriptionMaxLength).OverridePropertyName("Description");
        validator.RuleFor(c => permissions(c)).NotNull().OverridePropertyName("Permissions");
        validator.RuleForEach(c => permissions(c))
            .Must(Permissions.IsKnown).WithMessage("'{PropertyValue}' is not a known permission.")
            .OverridePropertyName("Permissions");
    }

    public static string? CleanDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
