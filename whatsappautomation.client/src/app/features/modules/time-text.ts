/** "07:15:00 AM": the exact time, the same everywhere in the app and the website. */
export function clockText(value: string | Date): string {
  return new Date(value).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true }).toUpperCase();
}

/** "Today 07:15:00 AM", "Tomorrow 07:15:00 AM" or "Fri 21 Nov 07:15:00 AM". */
export function whenText(value: string): string {
  const t = new Date(value);
  const now = new Date();
  const days = Math.round(
    (new Date(t.getFullYear(), t.getMonth(), t.getDate()).getTime() - new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()) /
      86_400_000,
  );
  const day = days === 0 ? 'Today' : days === 1 ? 'Tomorrow' : t.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short' });
  return `${day} ${clockText(t)}`;
}

/** "07:15" → "07:15 AM". */
export function hhmmText(hhmm: string | null): string {
  const [h, m] = (hhmm ?? '00:00').split(':').map((x) => Number(x));
  return new Date(2000, 0, 1, h || 0, m || 0).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', hour12: true }).toUpperCase();
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0]!.toUpperCase())
    .join('');
}

/** Status of one message as a dot and a short text, the same on every screen. */
export function statusLook(status: string | null): { text: string; tone: 'good' | 'warn' | 'bad' | 'off' | 'info' } {
  switch (status) {
    case 'read':
      return { text: 'Read', tone: 'good' };
    case 'delivered':
      return { text: 'Delivered', tone: 'good' };
    case 'sent':
      return { text: 'Sent', tone: 'info' };
    case 'pending':
      return { text: 'Waiting', tone: 'warn' };
    case 'failed':
      return { text: 'Failed', tone: 'bad' };
    case 'skipped':
      return { text: 'Skipped', tone: 'off' };
    default:
      return { text: 'Not sent', tone: 'off' };
  }
}

export const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
