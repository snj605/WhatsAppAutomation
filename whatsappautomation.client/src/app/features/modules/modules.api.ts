import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Shapes of /api/v1/modules. They mirror Application/Features/Modules/ModuleDtos.cs. */
export type ModuleKind = 'birthday' | 'yearly' | 'once';

export interface OccasionModule {
  id: string;
  name: string;
  kind: ModuleKind;
  /** When set, each person's entry has a second name with this label, used as {{for_name}}. */
  forNameLabel: string | null;
  autoSend: boolean;
  /** "HH:mm". */
  time: string | null;
  templateId: string | null;
  templateName: string | null;
  nextRunAt: string | null;
  todayCount: number;
  timeZone: string;
}

export interface ModuleDayPerson {
  contactId: string;
  name: string;
  phoneNumber: string;
  /** yyyy-MM-dd */
  date: string;
  daysUntil: number;
  years: number | null;
  forName: string | null;
  optedOut: boolean;
  status: 'not_sent' | 'pending' | 'sent' | 'delivered' | 'read' | 'failed' | 'skipped' | null;
  sentAt: string | null;
  deliveredAt: string | null;
}

export interface ModuleEntry {
  contactId: string;
  name: string;
  phoneNumber: string;
  day: number;
  month: number;
  year: number | null;
  forName: string | null;
}

export interface ModuleEntriesPage {
  items: ModuleEntry[];
  total: number;
}

@Injectable({ providedIn: 'root' })
export class ModulesApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<OccasionModule[]>('/api/v1/modules'));
  create = (name: string, kind: ModuleKind, forNameLabel: string | null) =>
    firstValueFrom(this.http.post<OccasionModule>('/api/v1/modules', { name, kind, forNameLabel }));
  update = (id: string, name: string, forNameLabel: string | null) =>
    firstValueFrom(this.http.put<OccasionModule>(`/api/v1/modules/${id}`, { name, forNameLabel }));
  remove = (id: string) => firstValueFrom(this.http.delete<void>(`/api/v1/modules/${id}`));
  setSending = (id: string, enabled: boolean, time: string | null, templateId: string | null) =>
    firstValueFrom(this.http.put<OccasionModule>(`/api/v1/modules/${id}/sending`, { enabled, time, templateId }));
  days = (id: string, days = 7) => firstValueFrom(this.http.get<ModuleDayPerson[]>(`/api/v1/modules/${id}/days`, { params: { days } }));
  entries = (id: string, search: string, page = 1, pageSize = 50) =>
    firstValueFrom(this.http.get<ModuleEntriesPage>(`/api/v1/modules/${id}/entries`, { params: { search, page, pageSize } }));
  setEntry = (id: string, contactId: string, entry: { day: number; month: number; year: number | null; forName: string | null }) =>
    firstValueFrom(this.http.put<void>(`/api/v1/modules/${id}/entries/${contactId}`, entry));
  removeEntry = (id: string, contactId: string) => firstValueFrom(this.http.delete<void>(`/api/v1/modules/${id}/entries/${contactId}`));
}

/** The signed-in user's modules, shared by the menu and the screens (reloaded after each change). */
@Injectable({ providedIn: 'root' })
export class ModulesStore {
  private readonly api = inject(ModulesApi);
  private readonly items = signal<OccasionModule[]>([]);
  readonly modules = this.items.asReadonly();
  readonly loaded = signal(false);
  readonly todayTotal = computed(() => this.items().reduce((n, m) => n + m.todayCount, 0));

  async reload(): Promise<void> {
    try {
      this.items.set(await this.api.list());
    } catch {
      // The menu just keeps what it had; each screen shows its own error.
    } finally {
      this.loaded.set(true);
    }
  }

  clear(): void {
    this.items.set([]);
    this.loaded.set(false);
  }

  byId(id: string): OccasionModule | undefined {
    return this.items().find((m) => m.id === id);
  }
}

/** A good icon for a module, by its kind or name. */
export function moduleIcon(m: Pick<OccasionModule, 'kind' | 'name'>): string {
  if (m.kind === 'birthday') return 'pi pi-gift';
  const name = m.name.toLowerCase();
  if (/asthi|shraddh|punya|memor|death|remember/.test(name)) return 'pi pi-sun';
  if (/anniv|marri|wedding/.test(name)) return 'pi pi-heart';
  return m.kind === 'once' ? 'pi pi-calendar' : 'pi pi-star';
}
