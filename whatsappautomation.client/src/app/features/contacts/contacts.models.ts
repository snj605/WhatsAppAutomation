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

/** One of the contact's occasion dates (Birthday, Anniversary, Asthi, …) for one of the user's own occasions. */
export interface ContactOccasion {
  moduleId: string;
  day: number;
  month: number;
  /** Needed for one-time occasions; optional otherwise. */
  year: number | null;
  /** The second name, for occasions that have one ({{for_name}}). */
  forName: string | null;
}

/** One of the user's own contact fields (GET /api/v1/contact-fields). */
export type ContactFieldType = 'text' | 'number' | 'date' | 'choice';

export interface ContactField {
  id: string;
  name: string;
  /** Used in messages as {{key}}. */
  key: string;
  type: ContactFieldType;
  options: string[];
  contactCount: number;
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
  /** Dates of the user's occasions (only the ones this contact has). */
  occasions: ContactOccasion[];
  /** Custom field values by field id, as stored (numbers with a dot, dates as yyyy-MM-dd). */
  fields: Record<string, string>;
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
  /** All of the contact's occasion dates; leave out to keep them as they are. */
  occasions?: ContactOccasion[];
  notes: string | null;
  groupIds: string[];
  /** Field values by field id; an empty value removes it; leave out to keep them. */
  fields?: Record<string, string | null>;
}

export interface ContactFilter {
  search?: string;
  categoryId?: string | null;
  groupId?: string | null;
  needsDetails?: boolean;
  /** Leave out for the normal list (without numbers not on WhatsApp); 'not_on_whatsapp' lists only those. */
  whatsApp?: 'not_on_whatsapp' | 'all' | null;
  /** Only contacts with a value in this custom field (text: containing fieldValue; others: equal to it). */
  fieldId?: string | null;
  fieldValue?: string | null;
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
