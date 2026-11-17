/** Shapes of the /api/v1 contacts endpoints (see ContactsController on the server). */

export type LanguageCode = 'en' | 'hi' | 'gu';

export const LANGUAGES: { code: LanguageCode; label: string; native: string }[] = [
  { code: 'en', label: 'English', native: 'English' },
  { code: 'hi', label: 'Hindi', native: 'हिन्दी' },
  { code: 'gu', label: 'Gujarati', native: 'ગુજરાતી' },
];

export const languageLabel = (code: string) => LANGUAGES.find((l) => l.code === code)?.label ?? code;

export interface Category {
  id: string;
  name: string;
  color: string;
  contactCount: number;
}

export interface Group {
  id: string;
  name: string;
  contactCount: number;
}

export interface Birthday {
  day: number;
  month: number;
  year: number | null;
}

export interface Contact {
  id: string;
  phoneNumber: string;
  savedName: string | null;
  nameEn: string | null;
  nameHi: string | null;
  nameGu: string | null;
  language: LanguageCode;
  categoryId: string | null;
  salutation: string | null;
  birthday: Birthday | null;
  notes: string | null;
  groups: { id: string; name: string }[];
  needsDetails: boolean;
  createdAt: string;
  updatedAt: string;
  /** Set when the user marked the person as not wanting broadcasts: broadcasts skip them. */
  optedOutAt: string | null;
  /** Whether the number has WhatsApp. Numbers without it are kept apart: no groups, no messages. */
  whatsAppCheck: WhatsAppCheck;
  whatsAppCheckedAt: string | null;
}

/**
 * unchecked: saved before the check existed (checked when the user asks). pending: checked automatically while
 * WhatsApp is connected.
 */
export type WhatsAppCheck = 'unchecked' | 'pending' | 'on_whatsapp' | 'not_on_whatsapp';

/** GET/POST /api/v1/contacts/whatsapp-check */
export interface WhatsAppCheckSummary {
  unchecked: number;
  pending: number;
  onWhatsApp: number;
  notOnWhatsApp: number;
  /** The user's WhatsApp is connected, so waiting numbers are being checked now. */
  connected: boolean;
}

/** A contact with a birthday today or soon (GET /api/v1/birthdays). */
export interface UpcomingBirthday {
  contactId: string;
  name: string;
  phoneNumber: string;
  language: LanguageCode;
  /** yyyy-MM-dd of the coming birthday. */
  date: string;
  /** 0 = today, 1 = tomorrow, … */
  daysUntil: number;
  turnsAge: number | null;
  optedOut: boolean;
  /** Today only: not_sent, pending, sent, delivered, read, failed or skipped. Null for coming days. */
  status: string | null;
  sentAt: string | null;
  deliveredAt: string | null;
}

/** Automatic birthday wishes (GET/PUT /api/v1/birthdays/settings). */
export interface BirthdaySettings {
  enabled: boolean;
  /** "HH:mm", like "07:15". */
  time: string | null;
  templateId: string | null;
  templateName: string | null;
  /** When the next automatic wishes start; null when off or not set up. */
  nextRunAt: string | null;
  timeZone: string;
}

export interface ContactPage {
  items: Contact[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ContactInput {
  phoneNumber: string;
  savedName: string | null;
  nameEn: string | null;
  nameHi: string | null;
  nameGu: string | null;
  language: LanguageCode;
  categoryId: string | null;
  salutation: string | null;
  birthday: Birthday | null;
  notes: string | null;
  groupIds: string[];
}

export interface ContactFilter {
  search?: string;
  categoryId?: string | null;
  groupId?: string | null;
  needsDetails?: boolean;
  /** Leave out for the normal list (without numbers not on WhatsApp); 'not_on_whatsapp' lists only those. */
  whatsApp?: 'not_on_whatsapp' | 'all' | null;
  page: number;
  pageSize: number;
}

export interface ImportRow {
  row: number;
  phoneNumber: string | null;
  name: string | null;
  category: string | null;
  action: 'create' | 'update' | 'error';
  errors: string[];
}

export interface ImportResult {
  applied: boolean;
  created: number;
  updated: number;
  failed: number;
  newCategories: string[];
  newGroups: string[];
  rows: ImportRow[];
}

/** The name a person is shown by in lists: the saved name, else the real name in their language. */
export function displayName(c: Contact): string {
  const real = c.language === 'hi' ? c.nameHi : c.language === 'gu' ? c.nameGu : c.nameEn;
  return c.savedName ?? real ?? c.nameEn ?? c.nameGu ?? c.nameHi ?? c.phoneNumber;
}

export function realName(c: Contact): string | null {
  return c.language === 'hi' ? c.nameHi : c.language === 'gu' ? c.nameGu : c.nameEn;
}

export const CATEGORY_COLORS = ['#10b981', '#3b82f6', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899', '#14b8a6', '#64748b'];
