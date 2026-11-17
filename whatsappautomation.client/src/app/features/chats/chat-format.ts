import { BroadcastSummary } from '../broadcasts/broadcasts.models';

/** Up to two initials, for the round avatar. */
export function initialsOf(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts[parts.length - 1]?.[0] ?? '') : '')).toUpperCase() || '?';
}

/** The grey line under a chat's name: where it stands, in a few words. */
export function chatSubtitle(c: BroadcastSummary): string {
  // "sent" already includes the delivered and read ones (those are subsets of it).
  const sent = c.counts.sent;
  const willGet = c.counts.total - c.counts.skipped;
  switch (c.status) {
    case 'sending':
      return `Sending · ${sent} of ${willGet}`;
    case 'paused':
      return `Paused · ${c.pauseReason ?? `${sent} of ${willGet} sent`}`;
    case 'cancelled':
      return `Stopped · ${sent} sent`;
    default:
      return `Done · ${sent} sent · ${c.counts.read} read`;
  }
}

/** Soft background and text colour for a person's avatar, always the same for the same name. */
export function avatarColors(name: string): { bg: string; fg: string } {
  let hash = 0;
  for (const ch of name) hash = (hash * 31 + ch.codePointAt(0)!) | 0;
  const hue = Math.abs(hash) % 360;
  return { bg: `hsl(${hue} 70% 93%)`, fg: `hsl(${hue} 45% 32%)` };
}
