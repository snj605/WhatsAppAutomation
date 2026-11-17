import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { DialogModule } from 'primeng/dialog';
import { problemMessage } from '../../core/http/problem';
import { initialsOf } from '../chats/chat-format';
import { Tenant, TenantAdmin, TenantsApi } from './tenants.api';

/**
 * Tenants (platform admin): every tenant with counts, and for the chosen one its admins. Never shows a tenant's
 * contacts or messages. Same screen as the app's Tenants page.
 */
@Component({
  selector: 'app-tenants-page',
  imports: [DatePipe, FormsModule, DialogModule],
  templateUrl: './tenants.page.html',
  styleUrls: ['../../shared/approved.css', './tenants.page.css'],
})
export class TenantsPage implements OnInit {
  private readonly api = inject(TenantsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly tenants = signal<Tenant[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => this.tenants().find((t) => t.id === this.selectedId()) ?? null);
  protected readonly admins = signal<TenantAdmin[]>([]);
  protected readonly totals = computed(() => {
    const list = this.tenants();
    return {
      tenants: list.length,
      active: list.filter((t) => t.isActive).length,
      users: list.reduce((n, t) => n + t.users, 0),
    };
  });
  protected editName = '';

  protected readonly createOpen = signal(false);
  protected create = { name: '', adminUserName: '', adminDisplayName: '', adminPassword: '' };
  protected readonly createError = signal<string | null>(null);

  protected readonly adminOpen = signal(false);
  protected newAdmin = { userName: '', displayName: '', password: '' };
  protected readonly adminError = signal<string | null>(null);

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected initials(name: string): string {
    return initialsOf(name);
  }

  protected async choose(t: Tenant): Promise<void> {
    this.selectedId.set(t.id);
    this.editName = t.name;
    await this.loadAdmins();
  }

  protected async saveName(t: Tenant): Promise<void> {
    const name = this.editName.trim();
    if (!name || name === t.name) return;
    await this.run(() => this.api.rename(t.id, name), 'Name saved');
  }

  protected toggleActive(t: Tenant): void {
    const off = t.isActive;
    this.confirm.confirm({
      header: off ? `Turn off ${t.name}?` : `Turn on ${t.name}?`,
      message: off
        ? 'Everyone in it is signed out and cannot sign in, and nothing is sent for it. Nothing is deleted; turn it on again at any time.'
        : 'Its people can sign in again, and waiting messages go out.',
      acceptLabel: off ? 'Turn off' : 'Turn on',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: off ? 'danger' : 'primary' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => void this.run(() => this.api.setActive(t.id, !off), off ? 'Turned off' : 'Turned on'),
    });
  }

  protected remove(t: Tenant): void {
    this.confirm.confirm({
      header: `Delete ${t.name}?`,
      message: `All ${t.users} people of this tenant are removed for good, with all their contacts, templates and messages, and their WhatsApp numbers are logged out. This cannot be undone.`,
      acceptLabel: 'Delete for good',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        void this.run(async () => {
          await this.api.remove(t.id);
          this.selectedId.set(null);
        }, 'Deleted'),
    });
  }

  protected openCreate(): void {
    this.create = { name: '', adminUserName: '', adminDisplayName: '', adminPassword: '' };
    this.createError.set(null);
    this.createOpen.set(true);
  }

  protected async saveCreate(): Promise<void> {
    const v = {
      name: this.create.name.trim(),
      adminUserName: this.create.adminUserName.trim(),
      adminDisplayName: this.create.adminDisplayName.trim(),
      adminPassword: this.create.adminPassword,
    };
    if (!v.name || !v.adminDisplayName || !/^[a-zA-Z0-9._-]{1,50}$/.test(v.adminUserName)) {
      this.createError.set('Fill in the tenant name, the admin\'s name and a user name (letters, digits, ".", "_" or "-").');
      return;
    }

    if (v.adminPassword.length < 12) {
      this.createError.set('The admin\'s first password needs at least 12 characters.');
      return;
    }

    this.saving.set(true);
    this.createError.set(null);
    try {
      const { id } = await this.api.create(v);
      this.createOpen.set(false);
      this.toast.add({ severity: 'success', summary: 'Tenant added', detail: `${v.adminDisplayName} chooses their own password at the first sign-in.` });
      await this.load();
      const added = this.tenants().find((t) => t.id === id);
      if (added) await this.choose(added);
    } catch (e) {
      this.createError.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected openAddAdmin(): void {
    this.newAdmin = { userName: '', displayName: '', password: '' };
    this.adminError.set(null);
    this.adminOpen.set(true);
  }

  protected async saveAdmin(t: Tenant): Promise<void> {
    const v = { userName: this.newAdmin.userName.trim(), displayName: this.newAdmin.displayName.trim(), password: this.newAdmin.password };
    if (!v.displayName || !/^[a-zA-Z0-9._-]{1,50}$/.test(v.userName) || v.password.length < 12) {
      this.adminError.set('Fill in a name, a user name and a first password of at least 12 characters.');
      return;
    }

    this.saving.set(true);
    this.adminError.set(null);
    try {
      await this.api.addAdmin(t.id, v);
      this.adminOpen.set(false);
      this.toast.add({ severity: 'success', summary: 'Admin added' });
      await this.load();
    } catch (e) {
      this.adminError.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  protected toggleAdmin(t: Tenant, a: TenantAdmin): void {
    void this.run(() => this.api.setAdminActive(t.id, a.id, !a.isActive), a.isActive ? 'Turned off' : 'Turned on');
  }

  protected readonly resetFor = signal<TenantAdmin | null>(null);
  protected resetPassword = '';

  protected resetAdmin(a: TenantAdmin): void {
    this.resetPassword = '';
    this.resetFor.set(a);
  }

  protected async saveReset(t: Tenant): Promise<void> {
    const a = this.resetFor();
    if (!a || this.resetPassword.length < 12) return;
    const password = this.resetPassword;
    this.resetFor.set(null);
    await this.run(() => this.api.resetAdminPassword(t.id, a.id, password), 'New password set');
  }

  protected removeAdmin(t: Tenant, a: TenantAdmin): void {
    this.confirm.confirm({
      header: `Delete ${a.displayName}?`,
      message: 'The account is removed for good, with everything that is theirs. This cannot be undone.',
      acceptLabel: 'Delete for good',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => void this.run(() => this.api.removeAdmin(t.id, a.id), 'Deleted'),
    });
  }

  private async load(): Promise<void> {
    try {
      this.tenants.set(await this.api.list());
      await this.loadAdmins();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load', detail: problemMessage(e), life: 6000 });
    } finally {
      this.loading.set(false);
    }
  }

  private async loadAdmins(): Promise<void> {
    const id = this.selectedId();
    this.admins.set(id && this.tenants().some((t) => t.id === id) ? await this.api.admins(id) : []);
  }

  private async run(action: () => Promise<unknown>, done: string): Promise<void> {
    this.saving.set(true);
    try {
      await action();
      this.toast.add({ severity: 'success', summary: done });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
    } finally {
      this.saving.set(false);
      await this.load();
    }
  }
}
