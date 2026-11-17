import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { MultiSelectModule } from 'primeng/multiselect';
import { PasswordModule } from 'primeng/password';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { AuthService } from '../../../core/auth/auth.service';
import { problemMessage } from '../../../core/http/problem';
import { SettingsApi } from '../settings.api';
import { Role, UserDetails } from '../settings.models';

/** Settings → Users: list users, create one, change roles, enable or disable. */
@Component({
  selector: 'app-users-page',
  imports: [
    DatePipe, FormsModule, ReactiveFormsModule, ButtonModule, DialogModule, InputTextModule,
    MultiSelectModule, PasswordModule, TableModule, TagModule,
  ],
  templateUrl: './users.page.html',
  styleUrl: '../settings.css',
})
export class UsersPage implements OnInit {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  protected readonly auth = inject(AuthService);

  readonly users = signal<UserDetails[]>([]);
  readonly roles = signal<Role[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly canManage = computed(() => {
    this.auth.user();
    return this.auth.can('users.manage');
  });

  readonly createOpen = signal(false);
  readonly createForm = inject(NonNullableFormBuilder).group({
    userName: ['', [Validators.required, Validators.maxLength(50), Validators.pattern(/^[a-zA-Z0-9._-]+$/)]],
    displayName: ['', [Validators.required, Validators.maxLength(100)]],
    password: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(1024)]],
    roleIds: [[] as string[], [Validators.required]],
  });

  readonly editing = signal<UserDetails | null>(null);
  editRoleIds: string[] = [];

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      // Role names are needed for the role pickers; loading roles needs roles.view.
      const [users, roles] = await Promise.all([
        this.api.users(),
        this.auth.can('roles.view') ? this.api.roles() : Promise.resolve([] as Role[]),
      ]);
      this.users.set(users);
      this.roles.set(roles);
    } catch (e) {
      this.showError(e);
    } finally {
      this.loading.set(false);
    }
  }

  openCreate(): void {
    this.createForm.reset();
    this.createOpen.set(true);
  }

  async create(): Promise<void> {
    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    await this.run(async () => {
      const value = this.createForm.getRawValue();
      await this.api.createUser({ ...value, userName: value.userName.trim(), displayName: value.displayName.trim() });
      this.createOpen.set(false);
      this.toast.add({ severity: 'success', summary: 'User created', detail: `${value.userName} can now sign in.` });
    });
  }

  openEditRoles(user: UserDetails): void {
    this.editRoleIds = user.roles.map((r) => r.id);
    this.editing.set(user);
  }

  async saveRoles(): Promise<void> {
    const user = this.editing();
    if (!user) {
      return;
    }

    await this.run(async () => {
      await this.api.setUserRoles(user.id, this.editRoleIds);
      this.editing.set(null);
      this.toast.add({ severity: 'success', summary: 'Roles saved', detail: user.displayName });
    });
  }

  toggleActive(user: UserDetails): void {
    const disabling = user.isActive;
    this.confirm.confirm({
      header: disabling ? 'Disable user?' : 'Enable user?',
      message: disabling
        ? `${user.displayName} will be signed out on every device and cannot sign in until enabled again.`
        : `${user.displayName} will be able to sign in again.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: disabling ? 'Disable' : 'Enable',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: disabling ? 'danger' : 'primary' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(async () => {
          await this.api.setUserActive(user.id, !disabling);
          this.toast.add({ severity: 'success', summary: disabling ? 'User disabled' : 'User enabled', detail: user.displayName });
        }),
    });
  }

  deleteUser(user: UserDetails): void {
    this.confirm.confirm({
      header: `Delete ${user.displayName}?`,
      message:
        'The account is removed for good, with all its contacts, groups, templates and messages, and its WhatsApp number is ' +
        'logged out. This cannot be undone. To only stop them signing in, disable the account instead.',
      icon: 'pi pi-trash',
      acceptLabel: 'Delete for good',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(async () => {
          await this.api.deleteUser(user.id);
          this.toast.add({ severity: 'success', summary: 'User deleted', detail: user.displayName });
        }),
    });
  }

  initialsOf(name: string): string {
    const parts = name.trim().split(/\s+/).filter(Boolean);
    return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? parts[parts.length - 1][0] : '')).toUpperCase() || '?';
  }

  isMe(user: UserDetails): boolean {
    return user.id === this.auth.user()?.id;
  }

  /** Saves, then reloads the list; shows the server's message if something is refused. */
  private async run(action: () => Promise<void>): Promise<void> {
    this.saving.set(true);
    try {
      await action();
      await this.load();
    } catch (e) {
      this.showError(e);
    } finally {
      this.saving.set(false);
    }
  }

  private showError(e: unknown): void {
    this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
  }
}
