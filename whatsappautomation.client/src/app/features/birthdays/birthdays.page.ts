import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from '../contacts/contacts.api';
import { BirthdaySettings, UpcomingBirthday } from '../contacts/contacts.models';
import { MessageTemplate, TemplatesApi } from '../templates/templates.api';
import { birthdayPreset } from './birthday-wishes';

interface BirthdayDay {
  daysUntil: number;
  date: Date;
  people: UpcomingBirthday[];
}

/** "07:15:00 AM": the exact time, the same everywhere in the app and the website. */
export function clockText(value: string | Date): string {
  return new Date(value).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true }).toUpperCase();
}

/** "Today 07:15:00 AM", "Tomorrow 07:15:00 AM" or "Fri 21 Nov 07:15:00 AM". */
export function whenText(value: string): string {
  const t = new Date(value);
  const now = new Date();
  const days = Math.round((new Date(t.getFullYear(), t.getMonth(), t.getDate()).getTime() -
    new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()) / 86_400_000);
  const day = days === 0 ? 'Today' : days === 1 ? 'Tomorrow' : t.toLocaleDateString('en-IN', { weekday: 'short', day: 'numeric', month: 'short' });
  return `${day} ${clockText(t)}`;
}

/**
 * Birthdays, the same as the phone app: the automatic wishes, and everyone with a birthday from today to 7 days ahead,
 * day by day. Today's people show whether their wish went out and when it reached their phone. Tick people (or a
 * whole day with Select all) to send a wish by hand.
 */
@Component({
  selector: 'app-birthdays-page',
  imports: [FormsModule, RouterLink, ButtonModule, SkeletonModule, ToggleSwitchModule],
  templateUrl: './birthdays.page.html',
  styleUrl: './birthdays.page.css',
})
export class BirthdaysPage implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly templatesApi = inject(TemplatesApi);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);
  protected readonly auth = inject(AuthService);
  protected readonly clockText = clockText;
  protected readonly whenText = whenText;

  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly birthdays = signal<UpcomingBirthday[]>([]);
  protected readonly settings = signal<BirthdaySettings | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());

  // Settings panel
  protected readonly editing = signal(false);
  protected readonly saving = signal(false);
  protected readonly templates = signal<MessageTemplate[] | null>(null);
  protected editEnabled = false;
  protected editTime = '07:15';
  protected editTemplateId: string | null = null;

  protected readonly selectedCount = computed(() => this.selected().size);
  protected readonly canSend = computed(() => {
    this.auth.user();
    return this.auth.can('broadcasts.send');
  });
  protected readonly ready = computed(() => {
    const s = this.settings();
    return !!s && s.enabled && !!s.nextRunAt;
  });
  protected readonly days = computed<BirthdayDay[]>(() => {
    const now = new Date();
    return Array.from({ length: 8 }, (_, d) => ({
      daysUntil: d,
      date: new Date(now.getFullYear(), now.getMonth(), now.getDate() + d),
      people: this.birthdays().filter((b) => b.daysUntil === d),
    })).filter((day) => day.daysUntil === 0 || day.people.length > 0);
  });
  protected readonly comingCount = computed(() => this.birthdays().filter((b) => b.daysUntil > 0).length);
  protected readonly chosenTemplate = computed(() => this.templates()?.find((t) => t.id === this.editTemplateIdSignal()) ?? null);
  private readonly editTemplateIdSignal = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected async load(): Promise<void> {
    this.error.set(null);
    try {
      const [list, settings] = await Promise.all([this.api.birthdays(7), this.api.birthdaySettings()]);
      this.birthdays.set(list);
      this.settings.set(settings);
      const ids = new Set(list.map((b) => b.contactId));
      this.selected.set(new Set([...this.selected()].filter((id) => ids.has(id))));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  // ---------- Settings ----------

  protected async openSettings(): Promise<void> {
    const s = this.settings();
    this.editEnabled = s?.enabled ?? false;
    this.editTime = s?.time ?? '07:15';
    this.setTemplate(s?.templateId ?? null);
    this.editing.set(true);
    if (this.templates() === null) {
      try {
        const list = await this.templatesApi.list();
        this.templates.set(list);
        if (this.editTemplateId && !list.some((t) => t.id === this.editTemplateId)) this.setTemplate(null);
      } catch (e) {
        this.templates.set([]);
        this.showError('Could not load templates', e);
      }
    }
  }

  protected setTemplate(id: string | null): void {
    this.editTemplateId = id;
    this.editTemplateIdSignal.set(id);
  }

  protected async saveSettings(): Promise<void> {
    if (this.editEnabled && !this.editTemplateId) {
      this.toast.add({ severity: 'warn', summary: 'Choose the template to send.' });
      return;
    }

    this.saving.set(true);
    try {
      const saved = await this.api.saveBirthdaySettings(this.editEnabled, this.editTime || null, this.editTemplateId);
      this.settings.set(saved);
      this.editing.set(false);
      this.toast.add({
        severity: 'success',
        summary: saved.enabled && saved.nextRunAt ? 'Automatic wishes are on' : 'Automatic wishes are off',
        detail: saved.nextRunAt ? `Next: ${whenText(saved.nextRunAt)}` : undefined,
      });
    } catch (e) {
      this.showError('Not saved', e);
    } finally {
      this.saving.set(false);
    }
  }

  /** "07:15" → "07:15:00 AM". */
  protected timeOfDay(hhmm: string | null): string {
    const [h, m] = (hhmm ?? '00:00').split(':').map((x) => Number(x));
    return clockText(new Date(2000, 0, 1, h || 0, m || 0));
  }

  /** The example with a sample name, so the user sees spacing, new lines and links as people get them. */
  protected sample(text: string): string {
    return text
      .replace(/\{([^{}|]*)\|[^{}]*\}/g, '$1')
      .replace(/\{\{\s*(name|name_en|name_hi|name_gu)\s*\}\}/g, 'Ramesh Patel')
      .replace(/\{\{\s*salutation\s*\}\}/g, '')
      .replace(/ {2,}/g, ' ');
  }

  // ---------- List ----------

  protected dayTitle(day: BirthdayDay): string {
    const date = day.date.toLocaleDateString('en-IN', { weekday: 'long', day: 'numeric', month: 'short' });
    return day.daysUntil === 0 ? `Today · ${date}` : day.daysUntil === 1 ? `Tomorrow · ${date}` : date;
  }

  protected canPick(b: UpcomingBirthday): boolean {
    return !b.optedOut && this.canSend();
  }

  protected isSelected(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(b: UpcomingBirthday): void {
    if (!this.canPick(b)) return;
    const next = new Set(this.selected());
    if (!next.delete(b.contactId)) next.add(b.contactId);
    this.selected.set(next);
  }

  protected allPicked(day: BirthdayDay): boolean {
    const ids = day.people.filter((b) => this.canPick(b)).map((b) => b.contactId);
    return ids.length > 0 && ids.every((id) => this.selected().has(id));
  }

  protected hasPickable(day: BirthdayDay): boolean {
    return day.people.some((b) => this.canPick(b));
  }

  protected toggleDay(day: BirthdayDay): void {
    const ids = day.people.filter((b) => this.canPick(b)).map((b) => b.contactId);
    const all = this.allPicked(day);
    const next = new Set(this.selected());
    for (const id of ids) {
      if (all) next.delete(id);
      else next.add(id);
    }

    this.selected.set(next);
  }

  protected clearSelection(): void {
    this.selected.set(new Set());
  }

  /** What happens with this person's wish: today the real state, on coming days when it is planned. */
  protected state(b: UpcomingBirthday): { text: string; tone: string } | null {
    if (b.optedOut) return { text: 'No broadcasts', tone: 'muted' };
    const s = this.settings();
    if (b.daysUntil > 0) return this.ready() ? { text: `Goes out at ${this.timeOfDay(s!.time)}`, tone: 'info' } : null;
    switch (b.status === 'read' ? 'delivered' : b.status) {
      case 'delivered':
        return { text: b.deliveredAt ? `Delivered ${clockText(b.deliveredAt)}` : 'Delivered', tone: 'good' };
      case 'sent':
        return { text: b.sentAt ? `Sent ${clockText(b.sentAt)}` : 'Sent', tone: 'primary' };
      case 'pending':
        return { text: 'Waiting to send', tone: 'warn' };
      case 'failed':
        return { text: 'Failed', tone: 'bad' };
      case 'skipped':
        return { text: 'Skipped', tone: 'muted' };
      default:
        return this.ready() ? { text: `Goes out ${clockText(s!.nextRunAt!)}`, tone: 'info' } : { text: 'Not sent', tone: 'warn' };
    }
  }

  protected initials(name: string): string {
    return name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]!.toUpperCase())
      .join('');
  }

  protected sendWishes(): void {
    const people = this.birthdays().filter((b) => this.selected().has(b.contactId));
    if (people.length === 0) return;
    void this.router.navigate(['/chats/new'], { state: { preset: birthdayPreset(people) } });
  }

  private showError(summary: string, e: unknown): void {
    this.toast.add({ severity: 'error', summary, detail: problemMessage(e), life: 6000 });
  }
}
