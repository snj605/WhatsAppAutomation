import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from '../contacts/contacts.api';
import { Contact, displayName } from '../contacts/contacts.models';
import { avatarColors, initialsOf } from './chat-format';

const COLORS = ['#10b981', '#0ea5e9', '#6366f1', '#a855f7', '#ec4899', '#f43f5e', '#f97316', '#eab308', '#64748b'];
const PAGE = 200;

/**
 * Group or category info, like WhatsApp group info: rename (and colour for categories), see and remove members,
 * add members, or delete it. Deleting keeps the contacts.
 */
@Component({
  selector: 'app-chat-info-page',
  imports: [RouterLink],
  templateUrl: './chat-info.page.html',
  styleUrl: './chat-info.page.css',
})
export class ChatInfoPage {
  private readonly api = inject(ContactsApi);
  private readonly router = inject(Router);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly kind = input.required<'group' | 'category'>();
  readonly id = input.required<string>();

  protected readonly colors = COLORS;
  protected readonly initials = initialsOf;
  protected readonly avatar = avatarColors;
  protected readonly displayName = displayName;

  protected readonly name = signal('');
  protected readonly color = signal<string | null>(null);
  protected readonly editing = signal(false);
  protected readonly draftName = signal('');
  protected readonly members = signal<Contact[]>([]);
  protected readonly memberTotal = signal(0);
  protected readonly busy = signal(false);

  // Adding members
  protected readonly adding = signal(false);
  protected readonly query = signal('');
  protected readonly candidates = signal<Contact[]>([]);
  protected readonly toAdd = signal<ReadonlySet<string>>(new Set());
  protected readonly memberIds = computed(() => new Set(this.members().map((m) => m.id)));
  private searchTimer: ReturnType<typeof setTimeout> | undefined;

  protected readonly label = computed(() => (this.kind() === 'group' ? 'group' : 'category'));

  constructor() {
    effect(() => {
      this.kind();
      this.id();
      void this.load();
    });
  }

  protected startRename(): void {
    this.draftName.set(this.name());
    this.editing.set(true);
  }

  protected async saveName(): Promise<void> {
    const name = this.draftName().trim();
    if (!name) return;
    await this.run(async () => {
      if (this.kind() === 'group') await this.api.saveGroup(this.id(), name);
      else await this.api.saveCategory(this.id(), name, this.color() ?? COLORS[0]!);
      this.name.set(name);
      this.editing.set(false);
    }, 'Name saved');
  }

  protected async setColor(color: string): Promise<void> {
    await this.run(async () => {
      await this.api.saveCategory(this.id(), this.name(), color);
      this.color.set(color);
    }, 'Colour saved');
  }

  protected remove(c: Contact): void {
    void this.run(async () => {
      await this.change([c.id], false);
      await this.loadMembers();
    }, `${displayName(c)} removed`);
  }

  protected openAdd(): void {
    this.adding.set(true);
    this.toAdd.set(new Set());
    void this.search('');
  }

  protected onSearch(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => void this.search(value), 300);
  }

  protected toggleAdd(id: string): void {
    const next = new Set(this.toAdd());
    if (next.has(id)) next.delete(id);
    else next.add(id);
    this.toAdd.set(next);
  }

  protected async addSelected(): Promise<void> {
    const ids = [...this.toAdd()];
    if (ids.length === 0) return;
    await this.run(async () => {
      await this.change(ids, true);
      this.adding.set(false);
      await this.loadMembers();
    }, `${ids.length} added`);
  }

  protected deleteIt(): void {
    this.confirm.confirm({
      header: `Delete this ${this.label()}?`,
      message: `"${this.name()}" will be deleted. The contacts in it stay in your contacts. Messages already sent are not affected.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Keep',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        void this.run(async () => {
          if (this.kind() === 'group') await this.api.deleteGroup(this.id());
          else await this.api.deleteCategory(this.id());
          await this.router.navigate(['/chats']);
        }, 'Deleted'),
    });
  }

  private change(ids: string[], add: boolean): Promise<unknown> {
    return this.kind() === 'group' ? this.api.changeMembers(this.id(), ids, add) : this.api.changeCategoryMembers(this.id(), ids, add);
  }

  private async run(action: () => Promise<void>, done: string): Promise<void> {
    this.busy.set(true);
    try {
      await action();
      this.toast.add({ severity: 'success', summary: done });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not done', detail: problemMessage(e), life: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      if (this.kind() === 'group') {
        const g = (await this.api.groups()).find((x) => x.id === this.id());
        this.name.set(g?.name ?? '');
        this.color.set(null);
      } else {
        const c = (await this.api.categories()).find((x) => x.id === this.id());
        this.name.set(c?.name ?? '');
        this.color.set(c?.color ?? null);
      }

      await this.loadMembers();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load', detail: problemMessage(e), life: 6000 });
    }
  }

  private async loadMembers(): Promise<void> {
    const filter = this.kind() === 'group' ? { groupId: this.id() } : { categoryId: this.id() };
    const page = await this.api.contacts({ ...filter, page: 1, pageSize: PAGE });
    this.members.set(page.items);
    this.memberTotal.set(page.total);
  }

  private async search(value: string): Promise<void> {
    try {
      const page = await this.api.contacts({ search: value.trim(), page: 1, pageSize: PAGE });
      this.candidates.set(page.items);
    } catch {
      this.candidates.set([]);
    }
  }
}
