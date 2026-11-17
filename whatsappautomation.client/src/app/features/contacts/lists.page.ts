import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectButtonModule } from 'primeng/selectbutton';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { ContactsApi } from './contacts.api';
import { Category, CATEGORY_COLORS, Group, LANGUAGES, LanguageCode } from './contacts.models';

type Editing = { kind: 'category'; id: string | null; name: string; color: string } | { kind: 'group'; id: string | null; name: string };

/** Categories, groups and the default message language. */
@Component({
  selector: 'app-contact-lists-page',
  imports: [FormsModule, RouterLink, ButtonModule, DialogModule, InputTextModule, SelectButtonModule],
  templateUrl: './lists.page.html',
  styleUrls: ['./contacts.css', './lists.page.css'],
})
export class ContactListsPage implements OnInit {
  private readonly api = inject(ContactsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly auth = inject(AuthService);

  protected readonly colors = CATEGORY_COLORS;
  protected readonly languages = LANGUAGES.map((l) => ({ label: l.code === 'en' ? l.label : `${l.label} · ${l.native}`, value: l.code }));
  protected readonly categories = signal<Category[]>([]);
  protected readonly groups = signal<Group[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly editing = signal<Editing | null>(null);
  protected defaultLanguage: LanguageCode = 'en';
  protected readonly canManage = computed(() => {
    this.auth.user();
    return this.auth.can('contacts.manage');
  });

  async ngOnInit(): Promise<void> {
    await this.load();
    try {
      this.defaultLanguage = (await this.api.preferences()).defaultLanguage;
    } catch {
      // keep English
    }
  }

  protected newCategory(): void {
    const used = new Set(this.categories().map((c) => c.color));
    this.editing.set({ kind: 'category', id: null, name: '', color: CATEGORY_COLORS.find((c) => !used.has(c)) ?? CATEGORY_COLORS[0] });
  }

  protected editCategory(c: Category): void {
    this.editing.set({ kind: 'category', id: c.id, name: c.name, color: c.color });
  }

  protected newGroup(): void {
    this.editing.set({ kind: 'group', id: null, name: '' });
  }

  protected editGroup(g: Group): void {
    this.editing.set({ kind: 'group', id: g.id, name: g.name });
  }

  /** Creates Elders, Youngers and Others in one click. */
  protected async addSuggested(): Promise<void> {
    await this.run(async () => {
      await this.api.saveCategory(null, 'Elders', '#10b981');
      await this.api.saveCategory(null, 'Youngers', '#3b82f6');
      await this.api.saveCategory(null, 'Others', '#64748b');
      this.toast.add({ severity: 'success', summary: 'Categories created', detail: 'Elders, Youngers and Others' });
    });
  }

  protected async save(): Promise<void> {
    const e = this.editing();
    if (!e || !e.name.trim()) return;
    await this.run(async () => {
      if (e.kind === 'category') {
        await this.api.saveCategory(e.id, e.name.trim(), e.color);
      } else {
        await this.api.saveGroup(e.id, e.name.trim());
      }

      this.editing.set(null);
      this.toast.add({ severity: 'success', summary: 'Saved', detail: e.name.trim() });
    });
  }

  protected remove(kind: 'category' | 'group', item: Category | Group): void {
    this.confirm.confirm({
      header: kind === 'category' ? 'Delete category?' : 'Delete group?',
      message:
        kind === 'category'
          ? `"${item.name}" will be deleted. A category that contacts still use cannot be deleted.`
          : `"${item.name}" will be deleted. Its ${item.contactCount} contacts stay; they only leave the group.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(async () => {
          await (kind === 'category' ? this.api.deleteCategory(item.id) : this.api.deleteGroup(item.id));
          this.toast.add({ severity: 'success', summary: 'Deleted', detail: item.name });
        }),
    });
  }

  protected async saveLanguage(language: LanguageCode): Promise<void> {
    try {
      await this.api.setDefaultLanguage(language);
      this.toast.add({ severity: 'success', summary: 'Default language saved' });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [categories, groups] = await Promise.all([this.api.categories(), this.api.groups()]);
      this.categories.set(categories);
      this.groups.set(groups);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load', detail: problemMessage(e), life: 6000 });
    } finally {
      this.loading.set(false);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    try {
      await action();
      await this.load();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
      await this.load();
    } finally {
      this.saving.set(false);
    }
  }
}
