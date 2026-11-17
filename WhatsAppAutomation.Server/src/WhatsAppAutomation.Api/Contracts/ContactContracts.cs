namespace WhatsAppAutomation.Api.Contracts;

public sealed record SaveCategoryRequest(string Name, string Color);

public sealed record SaveGroupRequest(string Name);

/// <param name="Add">true adds the contacts to the group; false removes them from it.</param>
public sealed record GroupMembersRequest(IReadOnlyList<Guid> ContactIds, bool Add);

public sealed record DeleteContactsRequest(IReadOnlyList<Guid> Ids);

/// <param name="ContactIds">Contacts to check (again). Leave out to check every contact that was never checked.</param>
public sealed record WhatsAppCheckRequest(IReadOnlyList<Guid>? ContactIds);

public sealed record DeletedResponse(int Deleted);

/// <param name="OptedOut">true: no more broadcasts to this contact. false: allow broadcasts again.</param>
public sealed record SetOptOutRequest(bool OptedOut);

public sealed record SetPreferencesRequest(string DefaultLanguage);

