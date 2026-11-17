import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ConfirmationService, MessageService } from 'primeng/api';
import { ButtonModule } from 'primeng/button';
import { CheckboxModule } from 'primeng/checkbox';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { TableModule } from 'primeng/table';
import { TagModule } from 'primeng/tag';
import { TextareaModule } from 'primeng/textarea';
import { AuthService } from '../../../core/auth/auth.service';
import { problemMessage } from '../../../core/http/problem';
import { SettingsApi } from '../settings.api';
import { Permission, Role } from '../settings.models';

interface PermissionGroup {
  name: string;
  permissions: Permission[];
}

/** Settings → Roles: list roles, create and edit them with their permissions, delete unused ones. */
@Component({
  selector: 'app-roles-page',
  imports: [ReactiveFormsModule, ButtonModule, CheckboxModule, DialogModule, InputTextModule, TableModule, TagModule, TextareaModule],
  templateUrl: './roles.page.html',
  styleUrl: '../settings.css',
})
export class RolesPage implements OnInit {
  private readonly api = inject(SettingsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  private readonly auth = inject(AuthService);

  readonly roles = signal<Role[]>([]);
  readonly permissions = signal<Permission[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly canManage = computed(() => {
    this.auth.user();
    return this.auth.can('roles.manage');
  });

  /** Permissions grouped under their headings, in the order the server lists them. */
  readonly groups = computed<PermissionGroup[]>(() => {
    const groups = new Map<string, Permission[]>();
    for (const p of this.permissions()) {
      groups.set(p.group, [...(groups.get(p.group) ?? []), p]);
    }
    return [...groups].map(([name, permissions]) => ({ name, permissions }));
  });

  /** null = dialog closed, 'new' = creating, a Role = editing that role. */
  readonly editing = signal<Role | 'new' | null>(null);
  readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(50)]],
    description: ['', [Validators.maxLength(200)]],
    permissions: [[] as string[]],
  });

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [roles, permissions] = await Promise.all([this.api.roles(), this.api.permissions()]);
      this.roles.set(roles);
      this.permissions.set(permissions);
    } catch (e) {
      this.showError(e);
    } finally {
      this.loading.set(false);
    }
  }

  openCreate(): void {
    this.form.reset({ name: '', description: '', permissions: [] });
    this.editing.set('new');
  }

  openEdit(role: Role): void {
    this.form.reset({ name: role.name, description: role.description ?? '', permissions: [...role.permissions] });
    this.editing.set(role);
  }

  dialogTitle(): string {
    const editing = this.editing();
    return editing === 'new' ? 'New role' : `Edit ${editing?.name ?? ''}`;
  }

  async save(): Promise<void> {
    const editing = this.editing();
    if (this.form.invalid || editing === null) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const body = {
      name: value.name.trim(),
      description: value.description.trim() || null,
      permissions: value.permissions,
    };

    await this.run(async () => {
      if (editing === 'new') {
        await this.api.createRole(body);
      } else {
        await this.api.updateRole(editing.id, body);
      }
      this.editing.set(null);
      this.toast.add({ severity: 'success', summary: 'Role saved', detail: body.name });
    });
  }

  remove(role: Role): void {
    this.confirm.confirm({
      header: 'Delete role?',
      message: `Delete the role "${role.name}"? This cannot be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Delete',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(async () => {
          await this.api.deleteRole(role.id);
          this.toast.add({ severity: 'success', summary: 'Role deleted', detail: role.name });
        }),
    });
  }

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
