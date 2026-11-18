namespace WhatsAppAutomation.Api.Contracts;

/// <param name="Kind">"yearly" (every year) or "once"; "birthday" uses each contact's birthday.</param>
/// <param name="ForNameLabel">Optional: each person's entry gets a second name with this label, used as {{for_name}}.</param>
public sealed record CreateModuleRequest(string Name, string Kind, string? ForNameLabel);

public sealed record UpdateModuleRequest(string Name, string? ForNameLabel);

/// <param name="Time">"HH:mm", e.g. "07:15".</param>
public sealed record ModuleSendingRequest(bool Enabled, string? Time, Guid? TemplateId);

/// <param name="Year">Required for "once" modules.</param>
public sealed record ModuleEntryRequest(int Day, int Month, int? Year, string? ForName);

/// <param name="Type">"text", "number", "date" or "choice".</param>
/// <param name="Options">The choices of a choice field (at least two).</param>
public sealed record CreateContactFieldRequest(string Name, string Type, IReadOnlyList<string>? Options);

/// <param name="NameEn">For example Shri.</param>
/// <param name="NameGu">For example શ્રી.</param>
public sealed record SaveSalutationRequest(string NameEn, string NameGu);

public sealed record UpdateContactFieldRequest(string Name, IReadOnlyList<string>? Options);
