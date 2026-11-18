import { UploadedFile } from '../files/files.api';
import { ModuleDayPerson, OccasionModule } from './modules.api';

/** What the new-message screen receives through router state to start with people and a text ready. */
export interface ComposerPreset {
  name: string;
  people: { id: string; label: string }[];
  text: string;
  /** Wishes of an occasion module: they go before other messages and show on the module's page. */
  moduleId?: string | null;
  /** The module template's file, sent with the text. */
  attachment?: UploadedFile | null;
}

/** Ready-made wish when the module has no template. {a|b} gives each person one of the words; {{name}} is their name. */
export function defaultWish(m: Pick<OccasionModule, 'kind' | 'name' | 'forNameLabel'>): string {
  if (m.kind === 'birthday') return '{Happy birthday|Many happy returns of the day} {{name}}! 🎂 Wishing you a wonderful year ahead.';
  return m.forNameLabel ? `Dear {{name}}, remembering {{for_name}} today. 🙏` : `Dear {{name}}, warm wishes on your ${m.name.toLowerCase()}! 🎉`;
}

export function modulePreset(
  m: OccasionModule,
  people: ModuleDayPerson[],
  template: { text: string; attachment: UploadedFile | null } | null,
  today: Date = new Date(),
): ComposerPreset {
  const day = today.toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
  return {
    name: `${m.name} ${day}`,
    people: people.map((p) => ({ id: p.contactId, label: p.name })),
    text: template?.text ?? defaultWish(m),
    moduleId: m.id,
    attachment: template?.attachment ?? null,
  };
}
