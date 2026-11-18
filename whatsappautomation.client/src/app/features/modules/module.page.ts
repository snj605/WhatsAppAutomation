import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { SkeletonModule } from 'primeng/skeleton';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from '../contacts/contacts.api';
import { Contact, displayName } from '../contacts/contacts.models';
import { MessageTemplate, TemplatesApi } from '../templates/templates.api';
import { modulePreset } from './composer-preset';
import { ModuleDayPerson, ModuleEntry, moduleIcon, ModulesApi, ModulesStore, OccasionModule } from './modules.api';
import { hhmmText, initials, MONTHS, statusLook, whenText } from './time-text';

type Tab = 'days' | 'dates' | 'settings';

interface Day {
  daysUntil: number;
  date: Date;
  people: ModuleDayPerson[];
}

/**
 * One occasion module (Birthday, Anniversary, Asthi, ...), the same as on the phone:
 * - Next 7 days: everyone with the date, day by day, with what happened to today's wishes; tick people (or a whole
 *   day) and send by hand.
 * - Dates (Yearly and Once modules): who has which date, added by hand or from the Excel import.
 * - Settings: send by itself at a time with a template; rename; delete.
 */
@Component({
  selector: 'app-module-page',
  imports: [FormsModule, RouterLink, SkeletonModule],
  templateUrl: './module.page.html',
  styleUrls: ['../../shared/approved.css', './module.page.css'],
})
export class ModulePage {
  private readonly api = inject(ModulesApi);
  private readonly store = inject(ModulesStore);
  private readonly templatesApi = inject(TemplatesApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  protected readonly auth = inject(AuthService);

  /** Route parameter. */
  readonly id = input.required<string>();

  protected readonly whenText = whenText;
  protected readonly hhmmText = hhmmText;
  protected readonly initials = initials;
  protected readonly statusLook = statusLook;
  protected readonly months = MONTHS;
  protected readonly dayNumbers = Array.from({ length: 31 }, (_, i) => i + 1);
  protected readonly icon = moduleIcon;
  protected readonly nameOf = displayName;

  protected readonly tab = signal<Tab>('days');
  protected readonly module = signal<OccasionModule | null>(null);
  protected readonly people = signal<ModuleDayPerson[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly templates = signal<MessageTemplate[]>([]);

  protected readonly ready = computed(() => {
    const m = this.module();
    return !!m && m.autoSend && !!m.nextRunAt;
  });

  protected readonly days = computed<Day[]>(() => {
    const now = new Date();
    return Array.from({ length: 8 }, (_, d) => ({
      daysUntil: d,
      date: new Date(now.getFullYear(), now.getMonth(), now.getDate() + d),
      people: this.people().filter((p) => p.daysUntil === d),
    })).filter((day) => day.daysUntil === 0 || day.people.length > 0);
  });

  protected readonly comingCount = computed(() => this.people().filter((p) => p.daysUntil > 0).length);

  // ---------- Settings ----------
  protected editAuto = false;
  protected editTime = '07:15';
  protected editTemplateId: string | null = null;
  protected editName = '';
  protected editLabel = '';
  protected readonly saving = signal(false);

  // ---------- Dates ----------
  protected readonly entries = signal<ModuleEntry[]>([]);
  protected readonly entriesTotal = signal(0);
  protected readonly entriesPage = signal(1);
  protected readonly entrySearch = signal('');
  protected readonly loadingEntries = signal(false);
  /** The entry being added or changed (null = the form is closed). */
  protected readonly entryForm = signal<{ existing: boolean; contactId: string | null; contactName: string; day: number; month: number; year: number | null; forName: string } | null>(
    null,
  );
  protected readonly contactQuery = signal('');
  protected readonly contactHits = signal<Contact[]>([]);
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    // Only a new id (another occasion from the menu) loads again; nothing read while loading may re-run this,
    // or saving the settings (which refreshes the menu) would jump back to the first tab.
    effect(() => {
      const id = this.id();
      untracked(() => {
        this.tab.set('days');
        this.selected.set(new Set());
        this.entries.set([]);
        this.entryForm.set(null);
        void this.load(id);
      });
    });
  }

  protected async load(id = this.id()): Promise<void> {
    this.error.set(null);
    this.loading.set(true);
    try {
      if (!this.store.loaded()) await this.store.reload();
      let m = this.store.byId(id);
      if (!m) {
        await this.store.reload();
        m = this.store.byId(id);
      }

      if (!m) {
        this.error.set('This occasion was not found. It may have been deleted.');
        this.module.set(null);
        return;
      }

      this.module.set(m);
      const [people, templates] = await Promise.all([this.api.days(id, 7), this.templatesApi.list().catch(() => [] as MessageTemplate[])]);
      this.people.set(people);
      this.templates.set(templates);
      const ids = new Set(people.map((p) => p.contactId));
      this.selected.set(new Set([...this.selected()].filter((x) => ids.has(x))));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected setTab(t: Tab): void {
    this.tab.set(t);
    const m = this.module();
    if (!m) return;
    if (t === 'settings') {
      this.editAuto = m.autoSend;
      this.editTime = m.time ?? '07:15';
      this.editTemplateId = m.templateId;
      this.editName = m.name;
      this.editLabel = m.forNameLabel ?? '';
    } else if (t === 'dates' && this.entries().length === 0) {
      void this.loadEntries(1);
    }
  }

  // ---------- Next 7 days ----------

  protected dayTitle(day: Day): string {
    const date = day.date.toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'short' });
    return day.daysUntil === 0 ? `Today · ${date}` : day.daysUntil === 1 ? `Tomorrow · ${date}` : date;
  }

  protected canPick(p: ModuleDayPerson): boolean {
    return !p.optedOut;
  }

  protected isSelected(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(p: ModuleDayPerson): void {
    if (!this.canPick(p)) return;
    const next = new Set(this.selected());
    if (!next.delete(p.contactId)) next.add(p.contactId);
    this.selected.set(next);
  }

  protected pickable(day: Day): string[] {
    return day.people.filter((p) => this.canPick(p)).map((p) => p.contactId);
  }

  protected allPicked(day: Day): boolean {
    const ids = this.pickable(day);
    return ids.length > 0 && ids.every((id) => this.selected().has(id));
  }

  protected toggleDay(day: Day): void {
    const all = this.allPicked(day);
    const next = new Set(this.selected());
    for (const id of this.pickable(day)) {
      if (all) next.delete(id);
      else next.add(id);
    }

    this.selected.set(next);
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  /** The line under a person: their date, years and second name. */
  protected detail(p: ModuleDayPerson): string {
    const m = this.module();
    const parts: string[] = [];
    if (p.years) parts.push(m?.kind === 'birthday' ? `Turns ${p.years}` : `${p.years} years`);
    if (p.forName && m?.forNameLabel) parts.push(`${m.forNameLabel}: ${p.forName}`);
    parts.push(p.phoneNumber);
    return parts.join(' · ');
  }

  /** Today: what happened to the wish. Coming days: when it goes out by itself. */
  protected state(p: ModuleDayPerson): { text: string; tone: string } | null {
    if (p.optedOut) return { text: 'No broadcasts', tone: 'off' };
    const m = this.module();
    if (p.daysUntil > 0) return this.ready() ? { text: `Goes out ${hhmmText(m!.time)}`, tone: 'info' } : null;
    if (p.status === 'not_sent' || p.status === null) {
      return this.ready() ? { text: `Goes out ${whenText(m!.nextRunAt!)}`, tone: 'info' } : { text: 'Not sent', tone: 'warn' };
    }

    const look = statusLook(p.status);
    const at = p.status === 'delivered' || p.status === 'read' ? p.deliveredAt : p.status === 'sent' ? p.sentAt : null;
    return { text: at ? `${look.text} ${whenText(at)}` : look.text, tone: look.tone };
  }

  protected sendSelected(): void {
    const m = this.module();
    if (!m) return;
    const people = this.people().filter((p) => this.selected().has(p.contactId));
    if (people.length === 0) return;
    const template = this.templates().find((t) => t.id === m.templateId) ?? null;
    void this.router.navigate(['/chats/new'], { state: { preset: modulePreset(m, people, template) } });
  }

  // ---------- Settings ----------

  protected chosenTemplate(): MessageTemplate | null {
    return this.templates().find((t) => t.id === this.editTemplateId) ?? null;
  }

  /** The example with a sample name, so the user sees spacing, new lines and links as people get them. */
  protected sample(text: string): string {
    return text
      .replace(/\{([^{}|]*)\|[^{}]*\}/g, '$1')
      .replace(/\{\{\s*(name|name_en|name_hi|name_gu)\s*\}\}/g, 'Ramesh Patel')
      .replace(/\{\{\s*for_name\s*\}\}/g, 'Late Shri Mohanbhai')
      .replace(/\{\{\s*salutation\s*\}\}/g, '')
      .replace(/ {2,}/g, ' ');
  }

  protected async saveSending(): Promise<void> {
    const m = this.module();
    if (!m) return;
    if (this.editAuto && !this.editTemplateId) {
      this.toast.add({ severity: 'warn', summary: 'Choose the template to send.' });
      return;
    }

    this.saving.set(true);
    try {
      const saved = await this.api.setSending(m.id, this.editAuto, this.editTime || null, this.editTemplateId);
      this.module.set(saved);
      void this.store.reload();
      this.toast.add({
        severity: 'success',
        summary: saved.autoSend && saved.nextRunAt ? 'Automatic sending is on' : 'Automatic sending is off',
        detail: saved.nextRunAt ? `Next: ${whenText(saved.nextRunAt)}` : undefined,
      });
    } catch (e) {
      this.showError('Not saved', e);
    } finally {
      this.saving.set(false);
    }
  }

  protected async saveDetails(): Promise<void> {
    const m = this.module();
    if (!m) return;
    const name = this.editName.trim();
    if (!name) {
      this.toast.add({ severity: 'warn', summary: 'Give the occasion a name.' });
      return;
    }

    this.saving.set(true);
    try {
      const saved = await this.api.update(m.id, name, m.kind === 'birthday' ? null : this.editLabel.trim() || null);
      this.module.set(saved);
      await this.store.reload();
      this.toast.add({ severity: 'success', summary: 'Saved' });
    } catch (e) {
      this.showError('Not saved', e);
    } finally {
      this.saving.set(false);
    }
  }

  protected remove(): void {
    const m = this.module();
    if (!m) return;
    this.confirm.confirm({
      header: `Delete ${m.name}?`,
      message: 'Its dates are deleted and wishes still waiting are stopped. Contacts and sent messages stay.',
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        try {
          await this.api.remove(m.id);
          await this.store.reload();
          this.toast.add({ severity: 'success', summary: `${m.name} deleted` });
          void this.router.navigate(['/modules']);
        } catch (e) {
          this.showError('Not deleted', e);
        }
      },
    });
  }

  // ---------- Dates ----------

  protected async loadEntries(page: number): Promise<void> {
    const m = this.module();
    if (!m || m.kind === 'birthday') return;
    this.loadingEntries.set(true);
    try {
      const result = await this.api.entries(m.id, this.entrySearch().trim(), page, 50);
      this.entries.set(result.items);
      this.entriesTotal.set(result.total);
      this.entriesPage.set(page);
    } catch (e) {
      this.showError('Could not load the dates', e);
    } finally {
      this.loadingEntries.set(false);
    }
  }

  protected searchEntries(value: string): void {
    this.entrySearch.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.loadEntries(1), 350);
  }

  protected pages(): number {
    return Math.max(1, Math.ceil(this.entriesTotal() / 50));
  }

  protected dateText(e: Pick<ModuleEntry, 'day' | 'month' | 'year'>): string {
    return `${e.day} ${MONTHS[e.month - 1]}${e.year ? ' ' + e.year : ''}`;
  }

  protected openEntry(e: ModuleEntry | null): void {
    const now = new Date();
    this.contactQuery.set('');
    this.contactHits.set([]);
    this.entryForm.set(
      e
        ? { existing: true, contactId: e.contactId, contactName: e.name, day: e.day, month: e.month, year: e.year, forName: e.forName ?? '' }
        : { existing: false, contactId: null, contactName: '', day: now.getDate(), month: now.getMonth() + 1, year: this.module()?.kind === 'once' ? now.getFullYear() : null, forName: '' },
    );
  }

  protected findContacts(value: string): void {
    this.contactQuery.set(value);
    clearTimeout(this.searchTimer);
    if (value.trim().length < 2) {
      this.contactHits.set([]);
      return;
    }

    this.searchTimer = setTimeout(async () => {
      try {
        const page = await this.contactsApi.contacts({ search: value.trim(), page: 1, pageSize: 8 });
        this.contactHits.set(page.items);
      } catch {
        this.contactHits.set([]);
      }
    }, 300);
  }

  protected pickContact(c: Contact): void {
    const f = this.entryForm();
    if (!f) return;
    this.entryForm.set({ ...f, contactId: c.id, contactName: displayName(c) });
    this.contactHits.set([]);
    this.contactQuery.set('');
  }

  protected patchEntry(patch: Partial<{ day: number; month: number; year: number | null; forName: string }>): void {
    const f = this.entryForm();
    if (f) this.entryForm.set({ ...f, ...patch });
  }

  protected async saveEntry(): Promise<void> {
    const m = this.module();
    const f = this.entryForm();
    if (!m || !f) return;
    if (!f.contactId) {
      this.toast.add({ severity: 'warn', summary: 'Choose the person.' });
      return;
    }

    if (m.kind === 'once' && !f.year) {
      this.toast.add({ severity: 'warn', summary: 'A one-time date needs the year.' });
      return;
    }

    this.saving.set(true);
    try {
      await this.api.setEntry(m.id, f.contactId, { day: Number(f.day), month: Number(f.month), year: f.year ? Number(f.year) : null, forName: f.forName.trim() || null });
      this.entryForm.set(null);
      this.toast.add({ severity: 'success', summary: `Saved for ${f.contactName}` });
      await Promise.all([this.loadEntries(this.entriesPage()), this.load()]);
    } catch (e) {
      this.showError('Not saved', e);
    } finally {
      this.saving.set(false);
    }
  }

  protected removeEntry(e: ModuleEntry): void {
    const m = this.module();
    if (!m) return;
    this.confirm.confirm({
      header: `Remove ${e.name}?`,
      message: `Their date is taken out of ${m.name}. The contact stays.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Remove',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        try {
          await this.api.removeEntry(m.id, e.contactId);
          await Promise.all([this.loadEntries(this.entriesPage()), this.load()]);
        } catch (err) {
          this.showError('Not removed', err);
        }
      },
    });
  }

  protected kindText(m: OccasionModule): string {
    return m.kind === 'birthday' ? 'From each contact’s birthday' : m.kind === 'yearly' ? 'Every year on the same date' : 'One time, on a date with a year';
  }

  private showError(summary: string, e: unknown): void {
    this.toast.add({ severity: 'error', summary, detail: problemMessage(e), life: 6000 });
  }
}
