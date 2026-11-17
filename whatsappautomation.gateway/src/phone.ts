/**
 * Phone numbers arrive in E.164 form ("+919876543210"): the API checks them first.
 * WhatsApp wants the digits only.
 */
export function toDigits(e164: string): string | null {
  return /^\+[1-9]\d{7,14}$/.test(e164) ? e164.slice(1) : null;
}

/** "919876543210:12@s.whatsapp.net" → "+919876543210". Null for anything that is not a phone JID. */
export function phoneFromJid(jid: string | undefined | null): string | null {
  const match = /^(\d{8,15})(?::\d+)?@s\.whatsapp\.net$/.exec(jid ?? '');
  return match ? `+${match[1]}` : null;
}
