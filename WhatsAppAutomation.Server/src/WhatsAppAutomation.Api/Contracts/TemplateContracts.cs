namespace WhatsAppAutomation.Api.Contracts;

/// <param name="Text">The message; may use {{name}}, {{name_gu}}, {{name_hi}}, {{name_en}}, {{salutation}} and {a|b} choices.</param>
/// <param name="Shared">Put it in the tenant's library (tenant admins only).</param>
/// <param name="AttachmentId">A file uploaded (POST /api/v1/files) or taken from a link (POST /api/v1/files/from-link).</param>
public sealed record SaveTemplateRequest(string Name, string Text, bool Shared = false, Guid? AttachmentId = null);
