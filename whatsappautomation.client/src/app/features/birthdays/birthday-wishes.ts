import { UpcomingBirthday } from '../contacts/contacts.models';

/** Ready-made wish. {a|b} gives each person one of the words; {{name}} is their name. The user can change it. */
export const BIRTHDAY_TEXT = '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.';

/** What the new-message screen receives through router state to start with people and a text ready. */
export interface ComposerPreset {
  name: string;
  people: { id: string; label: string }[];
  text: string;
  /** All of them have their birthday today: the wishes then show in today's Birthdays chat. */
  forBirthday?: boolean;
}

export function birthdayPreset(people: UpcomingBirthday[], today: Date = new Date()): ComposerPreset {
  const day = today.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
  return {
    name: `Birthday wishes ${day}`,
    people: people.map((p) => ({ id: p.contactId, label: p.name })),
    text: BIRTHDAY_TEXT,
    forBirthday: people.length > 0 && people.every((p) => p.daysUntil === 0),
  };
}
