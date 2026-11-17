/** Shapes of /api/v1/broadcasts (see BroadcastsController on the server). */

export type BroadcastStatus = 'sending' | 'paused' | 'completed' | 'cancelled';
export type RecipientStatus = 'pending' | 'sent' | 'delivered' | 'read' | 'failed' | 'skipped';

export interface BroadcastInput {
  name: string;
  /** One message for everyone; {{name}}, {{name_en}}, {{name_hi}}, {{name_gu}}, {{salutation}} are filled per person. */
  text?: string | null;
  textEn: string | null;
  textHi: string | null;
  textGu: string | null;
  categoryIds: string[];
  groupIds: string[];
  contactIds: string[];
  /** Send to every contact (the lists are then not needed). */
  allContacts?: boolean;
  /** A file uploaded with POST /api/v1/files; the text is its caption. */
  attachmentId?: string | null;
  /** Birthday wishes: they show in today's Birthdays chat. */
  forBirthday?: boolean;
}

export interface BroadcastAttachment {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** False once the file was freed after sending. */
  kept: boolean;
}

export interface BroadcastPreview {
  total: number;
  willSend: number;
  byLanguage: Record<'en' | 'hi' | 'gu', number>;
  skipped: { contactId: string | null; name: string; phoneNumber: string; reason: string }[];
  samples: { language: 'en' | 'hi' | 'gu' | 'all'; contactName: string; text: string }[];
  dailyCap: number;
  estimatedDays: number;
}

export interface BroadcastCounts {
  total: number;
  pending: number;
  sent: number;
  delivered: number;
  read: number;
  failed: number;
  skipped: number;
}

export interface BroadcastSummary {
  id: string;
  name: string;
  status: BroadcastStatus;
  pauseReason: string | null;
  languages: string[];
  counts: BroadcastCounts;
  createdAt: string;
  finishedAt: string | null;
}

export interface Recipient {
  id: string;
  contactId: string | null;
  name: string;
  phoneNumber: string;
  language: string | null;
  text: string | null;
  status: RecipientStatus;
  reason: string | null;
  processedAt: string | null;
}

export interface BroadcastDetails {
  summary: BroadcastSummary;
  textEn: string | null;
  textHi: string | null;
  textGu: string | null;
  recipients: Recipient[];
  /** The one message for everyone (newer broadcasts); null for per-language ones. */
  text: string | null;
  attachment: BroadcastAttachment | null;
}

export interface Pacing {
  connected: boolean;
  sentToday: number;
  dailyCap: number;
  warmUpWeek: number | null;
  inSendingHours: boolean;
  sendingHours: string;
  nextSendAt: string | null;
  waiting: string | null;
}

export const BROADCAST_STATUS: Record<BroadcastStatus, { label: string; severity: 'success' | 'info' | 'warn' | 'secondary' | 'danger' }> = {
  sending: { label: 'Sending', severity: 'info' },
  paused: { label: 'Paused', severity: 'warn' },
  completed: { label: 'Completed', severity: 'success' },
  cancelled: { label: 'Cancelled', severity: 'secondary' },
};

export const RECIPIENT_STATUS: Record<RecipientStatus, { label: string; icon: string }> = {
  pending: { label: 'Waiting', icon: 'pi pi-clock' },
  sent: { label: 'Sent', icon: 'pi pi-check' },
  delivered: { label: 'Delivered', icon: 'pi pi-check-circle' },
  read: { label: 'Read', icon: 'pi pi-eye' },
  failed: { label: 'Failed', icon: 'pi pi-times-circle' },
  skipped: { label: 'Skipped', icon: 'pi pi-minus-circle' },
};

/** Share of the recipients that are finished (sent, failed or skipped), 0–100. */
export function progress(c: BroadcastCounts): number {
  return c.total === 0 ? 0 : Math.round(((c.total - c.pending) / c.total) * 100);
}
