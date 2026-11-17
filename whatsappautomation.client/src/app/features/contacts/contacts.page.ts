import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { IconFieldModule } from 'primeng/iconfield';
import { InputIconModule } from 'primeng/inputicon';
import { InputTextModule } from 'primeng/inputtext';
import { MenuModule } from 'primeng/menu';
import { PaginatorModule, PaginatorState } from 'primeng/paginator';
import { SelectModule } from 'primeng/select';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { ToggleSwitchModule } from 'primeng/toggleswitch';
import { TooltipModule } from 'primeng/tooltip';
import { MenuItem } from 'primeng/api';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { WhatsAppLiveService } from '../../core/realtime/whatsapp-live.service';
import { ContactDialog } from './contact-dialog';
import { ContactsApi } from './contacts.api';
import { Category, Contact, displayName, Group, languageLabel, LanguageCode, realName, WhatsAppCheckSummary } from './contacts.models';

/**
 * Contacts: search, filter by category or group, add/edit, and act on several at once. Numbers are checked on
 * WhatsApp automatically; those without WhatsApp are kept in their own list (no groups, no messages).
 */
@Component({
  selector: 'app-contacts-page',
  imports: [
    FormsModule, RouterLink, ButtonModule, IconFieldModule, InputIconModule, InputTextModule, MenuModule, PaginatorModule,
    SelectModule, TableModule, TagModule, ToggleSwitchModule, TooltipModule, ContactDialog,
  ],
  templateUrl: './contacts.page.html',
  styleUrl: './contacts.css',
})
export class ContactsPage implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);
  private readonly live = inject(WhatsAppLiveService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly displayName = displayName;
  protected readonly realName = realName;
  protected readonly languageLabel = languageLabel;

  protected readonly categories = signal<Category[]>([]);
  protected readonly groups = signal<Group[]>([]);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly total = signal(0);
  protected readonly loading = signal(true);
  protected readonly defaultLanguage = signal<LanguageCode>('en');
  protected readonly canManage = computed(() => {
    this.auth.user();
    return this.auth.can('contacts.manage');
  });

  // Filters
  protected search = '';
  protected readonly categoryId = signal<string | null>(null);
  protected groupId: string | null = null;
  protected needsDetails = false;
  /** false: the normal list. true: only numbers that are not on WhatsApp. */
  protected readonly notOnWhatsApp = signal(false);
  protected readonly check = signal<WhatsAppCheckSummary | null>(null);
  protected readonly checkBusy = signal(false);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);

  protected selection: Contact[] = [];
  protected readonly editing = signal<Contact | 'new' | null>(null);
  protected readonly allCount = computed(() => this.categories().reduce((sum, c) => sum + c.contactCount, 0));
  protected groupMenu: MenuItem[] = [];

  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private categoryById = new Map<string, Category>();

  async ngOnInit(): Promise<void> {
    this.groupId = this.route.snapshot.queryParamMap.get('group');
    // Results of the WhatsApp check arrive live.
    this.live.contacts$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((summary) => {
      this.check.set(summary);
      void this.load();
    });
    void this.live.start();
    this.destroyRef.onDestroy(() => void this.live.stop());
    await Promise.all([this.loadLists(), this.load(), this.loadCheck()]);
    try {
      this.defaultLanguage.set((await this.api.preferences()).defaultLanguage);
    } catch {
      // English stays the default.
    }
  }

  protected category(id: string | null): Category | undefined {
    return id ? this.categoryById.get(id) : undefined;
  }

  protected initials(c: Contact): string {
    const name = displayName(c).replace(/^\+/, '');
    const parts = name.trim().split(/\s+/).filter(Boolean);
    return [...(parts[0] ?? '?')][0].toUpperCase() + (parts.length > 1 ? [...parts[parts.length - 1]][0].toUpperCase() : '');
  }

  protected birthday(c: Contact): string | null {
    if (!c.birthday) return null;
    const month = new Date(2024, c.birthday.month - 1, 1).toLocaleString('en', { month: 'short' });
    return `${c.birthday.day} ${month}${c.birthday.year ? ' ' + c.birthday.year : ''}`;
  }

  protected onSearch(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.reload(), 300);
  }

  protected showNotOnWhatsApp(on: boolean): void {
    this.notOnWhatsApp.set(on);
    this.reload();
  }

  /** Checks every never-checked contact, or these contacts again. */
  protected async requestCheck(ids: string[] | null = null): Promise<void> {
    this.checkBusy.set(true);
    try {
      const summary = await this.api.requestWhatsAppCheck(ids);
      this.check.set(summary);
      this.toast.add({
        severity: 'info',
        summary: summary.connected ? 'Checking numbers on WhatsApp' : 'Will be checked when WhatsApp is connected',
        detail: summary.connected ? 'Numbers without WhatsApp move to "Not on WhatsApp".' : 'Link your number on the WhatsApp page.',
      });
      await this.load();
    } catch (e) {
      this.showError(e);
    } finally {
      this.checkBusy.set(false);
    }
  }

  protected pickCategory(id: string | null): void {
    this.categoryId.set(id);
    this.reload();
  }

  protected reload(): void {
    this.page.set(1);
    this.selection = [];
    void this.load();
  }

  protected onPage(state: PaginatorState): void {
    this.page.set((state.page ?? 0) + 1);
    this.pageSize.set(state.rows ?? 25);
    void this.load();
  }

  protected async onSaved(contact: Contact): Promise<void> {
    const adding = this.editing() === 'new';
    this.editing.set(null);
    await this.loadCheck();
    const checking = contact.whatsAppCheck === 'pending'
      ? this.check()?.connected
        ? ' · checking the number on WhatsApp…'
        : ' · the number is checked once WhatsApp is connected'
      : '';
    this.toast.add({ severity: 'success', summary: adding ? 'Contact added' : 'Contact saved', detail: displayName(contact) + checking });
    await Promise.all([this.loadLists(), this.load()]);
  }

  protected openGroupMenu(add: boolean, menu: { toggle: (e: Event) => void }, event: Event): void {
    this.groupMenu = this.groups().map((g) => ({
      label: g.name,
      icon: add ? 'pi pi-plus' : 'pi pi-minus',
      command: () => void this.changeMembers(g, add),
    }));
    if (this.groupMenu.length === 0) {
      this.groupMenu = [{ label: 'No groups yet', disabled: true }];
    }

    menu.toggle(event);
  }

  protected deleteOne(contact: Contact): void {
    this.confirmDelete([contact]);
  }

  /** Stops broadcasts to a contact, or allows them again when the person asks (asks first in both cases). */
  protected toggleOptOut(contact: Contact): void {
    const stop = !contact.optedOutAt;
    this.confirm.confirm({
      header: stop ? 'Stop broadcasts?' : 'Allow broadcasts again?',
      message: stop
        ? `${displayName(contact)} will be skipped in every broadcast until you allow them again.`
        : `${displayName(contact)} asked not to get broadcasts. Only allow this if they asked to get messages again.`,
      icon: stop ? 'pi pi-ban' : 'pi pi-check-circle',
      acceptLabel: stop ? 'Stop broadcasts' : 'Allow again',
      rejectLabel: 'Cancel',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        try {
          const saved = await this.api.setOptOut(contact.id, stop);
          this.contacts.update((list) => list.map((c) => (c.id === saved.id ? saved : c)));
          this.toast.add({ severity: 'success', summary: stop ? 'Broadcasts stopped' : 'Broadcasts allowed', detail: displayName(saved) });
        } catch (e) {
          this.showError(e);
        }
      },
    });
  }

  protected selectionIds(): string[] {
    return this.selection.map((c) => c.id);
  }

  protected deleteSelected(): void {
    this.confirmDelete(this.selection);
  }

  private confirmDelete(contacts: Contact[]): void {
    const one = contacts.length === 1;
    this.confirm.confirm({
      header: one ? 'Delete contact?' : `Delete ${contacts.length} contacts?`,
      message: one ? `${displayName(contacts[0])} will be removed. This cannot be undone.` : 'They will be removed. This cannot be undone.',
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        try {
          const { deleted } = await this.api.deleteContacts(contacts.map((c) => c.id));
          this.toast.add({ severity: 'success', summary: deleted === 1 ? 'Contact deleted' : `${deleted} contacts deleted` });
          this.selection = [];
          await Promise.all([this.loadLists(), this.load()]);
        } catch (e) {
          this.showError(e);
        }
      },
    });
  }

  private async changeMembers(group: Group, add: boolean): Promise<void> {
    try {
      await this.api.changeMembers(group.id, this.selection.map((c) => c.id), add);
      this.toast.add({
        severity: 'success',
        summary: add ? `Added to ${group.name}` : `Removed from ${group.name}`,
        detail: `${this.selection.length} contact${this.selection.length === 1 ? '' : 's'}`,
      });
      this.selection = [];
      await Promise.all([this.loadLists(), this.load()]);
    } catch (e) {
      this.showError(e);
    }
  }

  private async loadLists(): Promise<void> {
    try {
      const [categories, groups] = await Promise.all([this.api.categories(), this.api.groups()]);
      this.categories.set(categories);
      this.categoryById = new Map(categories.map((c) => [c.id, c]));
      this.groups.set(groups);
    } catch (e) {
      this.showError(e);
    }
  }

  private async loadCheck(): Promise<void> {
    try {
      this.check.set(await this.api.whatsAppCheck());
    } catch {
      // The list works without the counts.
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const page = await this.api.contacts({
        search: this.search,
        categoryId: this.categoryId(),
        groupId: this.groupId,
        needsDetails: this.needsDetails,
        whatsApp: this.notOnWhatsApp() ? 'not_on_whatsapp' : null,
        page: this.page(),
        pageSize: this.pageSize(),
      });
      this.contacts.set(page.items);
      this.total.set(page.total);
    } catch (e) {
      this.showError(e);
    } finally {
      this.loading.set(false);
    }
  }

  private showError(e: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Something went wrong', detail: problemMessage(e), life: 6000 });
  }
}
