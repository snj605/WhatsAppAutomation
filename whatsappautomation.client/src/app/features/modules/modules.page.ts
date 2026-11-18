import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { moduleIcon, ModuleKind, ModulesApi, ModulesStore, OccasionModule } from './modules.api';
import { hhmmText } from './time-text';

/** Ready-made ideas for the builder; the user can type any name. */
const IDEAS: { name: string; kind: ModuleKind; label: string | null }[] = [
  { name: 'Anniversary', kind: 'yearly', label: null },
  { name: 'Asthi', kind: 'yearly', label: 'In memory of' },
  { name: 'Punyatithi', kind: 'yearly', label: 'In memory of' },
  { name: 'Event', kind: 'once', label: null },
];

/**
 * All occasions of the user, and the builder for new ones. Members make their own; nothing is added for them.
 * Each occasion gets its own page with the next 7 days, its dates and automatic sending.
 */
@Component({
  selector: 'app-modules-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './modules.page.html',
  styleUrls: ['../../shared/approved.css', './modules.page.css'],
})
export class ModulesPage implements OnInit {
  private readonly api = inject(ModulesApi);
  protected readonly store = inject(ModulesStore);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);

  protected readonly icon = moduleIcon;
  protected readonly hhmmText = hhmmText;
  protected readonly ideas = IDEAS;
  protected readonly max = 20;

  protected readonly building = signal(false);
  protected readonly saving = signal(false);
  protected name = '';
  protected kind: ModuleKind = 'yearly';
  protected label = '';
  protected withSecondName = false;

  protected readonly hasBirthday = computed(() => this.store.modules().some((m) => m.kind === 'birthday'));

  async ngOnInit(): Promise<void> {
    await this.store.reload();
  }

  protected open(): void {
    this.name = '';
    this.kind = 'yearly';
    this.label = '';
    this.withSecondName = false;
    this.building.set(true);
  }

  protected useIdea(i: { name: string; kind: ModuleKind; label: string | null }): void {
    this.name = i.name;
    this.kind = i.kind;
    this.withSecondName = !!i.label;
    this.label = i.label ?? '';
  }

  protected kindText(m: OccasionModule): string {
    return m.kind === 'birthday' ? 'Birthdays' : m.kind === 'yearly' ? 'Every year' : 'One time';
  }

  protected async create(): Promise<void> {
    const name = this.name.trim();
    if (!name) {
      this.toast.add({ severity: 'warn', summary: 'Give the occasion a name.' });
      return;
    }

    if (this.withSecondName && !this.label.trim()) {
      this.toast.add({ severity: 'warn', summary: 'Name the second name, for example "In memory of".' });
      return;
    }

    this.saving.set(true);
    try {
      const created = await this.api.create(name, this.kind, this.kind !== 'birthday' && this.withSecondName ? this.label.trim() : null);
      await this.store.reload();
      this.building.set(false);
      this.toast.add({ severity: 'success', summary: `${created.name} is ready`, detail: 'Add dates, then choose when it sends.' });
      void this.router.navigate(['/modules', created.id]);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not created', detail: problemMessage(e), life: 6000 });
    } finally {
      this.saving.set(false);
    }
  }
}
