namespace WhatsAppAutomation.Api.Contracts;

/// <param name="Text">The message; may use {{name}}, {{name_gu}}, {{name_hi}}, {{name_en}}, {{salutation}} and {a|b} choices.</param>
public sealed record SaveTemplateRequest(string Name, string Text);
