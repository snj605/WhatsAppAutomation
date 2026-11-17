import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { DialogModule } from 'primeng/dialog';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/http/problem';
import { initialsOf } from '../chats/chat-format';
import { connectionStatus } from '../whatsapp/whatsapp.status';
import { MembersApi } from './members.api';
import { Member, MEMBER_SWITCHES } from './members.models';

type Dot = 'good' | 'warn' | 'bad' | 'off';

/**
 * Members (tenant admin): everyone in the tenant, and for the chosen person what they may do. Same screen as the
 * app's Members page.
 */
@Component({
  selector: 'app-members-page',
  imports: [FormsModule, RouterLink, DialogModule],
  templateUrl: './members.page.html',
  styleUrls: ['../../shared/approved.css', './members.page.css'],
})
export class MembersPage implements OnInit {
  private readonly api = inject(MembersApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);
  protected readonly auth = inject(AuthService);
  protected readonly switches = MEMBER_SWITCHES;

  protected readonly members = signal<Member[]>([]);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => this.members().find((m) => m.id === this.selectedId()) ?? null);
  protected readonly title = computed(() => {
    const count = this.members().length;
    return `${this.auth.user()?.tenantName ?? ''} · ${count} ${count === 1 ? 'person' : 'people'}`;
  });

  /** Edits of the chosen person, saved with Save. */
  protected editName = '';

  protected readonly addOpen = signal(false);
  protected add = { userName: '', displayName: '', password: '', isAdmin: false, switches: [] as string[] };
  protected readonly addError = signal<string | null>(null);

  protected readonly resetOpen = signal(false);
  protected resetPasswordValue = '';

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected isMe(m: Member): boolean {
    return m.id === this.auth.user()?.id;
  }

  protected initials(name: string): string {
    return initialsOf(name);
  }

  protected line(m: Member): { text: string; dot: Dot } {
    if (!m.isActive) return { text: 'Turned off', dot: 'off' };
    const status = connectionStatus(m.whatsAppStatus);
    const you = this.isMe(m) ? 'You · ' : '';
    if (m.whatsAppStatus === 'not_linked') return { text: `${you}WhatsApp not linked yet`, dot: 'warn' };
    const dot: Dot = status.severity === 'success' ? 'good' : status.severity === 'danger' ? 'bad' : 'warn';
    return { text: `${you}${status.label}${m.whatsAppNumber ? ' · ' + m.whatsAppNumber : ''}`, dot };
  }

  protected tag(m: Member): string {
    return !m.isActive ? 'Off' : m.kind === 'tenant_admin' ? 'Admin' : 'Member';
  }

  protected choose(m: Member): void {
    this.selectedId.set(m.id);
    this.editName = m.displayName;
  }

  protected has(m: Member, permission: string): boolean {
    return m.switches.includes(permission);
  }

  /** Switches save at once, like the app. */
  protected async toggleSwitch(m: Member, permission: string): Promise<void> {
    const switches = this.has(m, permission) ? m.switches.filter((s) => s !== permission) : [...m.switches, permission];
    await this.run(() => this.api.update(m.id, { displayName: m.displayName, isAdmin: false, switches }), null);
  }

  protected async toggleAdmin(m: Member): Promise<void> {
    const makeAdmin = m.kind !== 'tenant_admin';
    await this.run(
      () => this.api.update(m.id, { displayName: m.displayName, isAdmin: makeAdmin, switches: m.switches }),
      makeAdmin ? `${m.displayName} is now an admin` : `${m.displayName} is now a member`,
    );
  }

  protected async saveName(m: Member): Promise<void> {
    const name = this.editName.trim();
    if (!name || name === m.displayName) return;
    await this.run(
      () => this.api.update(m.id, { displayName: name, isAdmin: m.kind === 'tenant_admin', switches: m.switches }),
      'Name saved',
    );
  }

  protected toggleActive(m: Member): void {
    const off = m.isActive;
    this.confirm.confirm({
      header: off ? `Turn off ${m.displayName}?` : `Turn on ${m.displayName}?`,
      message: off
        ? 'They are signed out on every device, cannot sign in, and nothing is sent from their number. Nothing is deleted; turn them on again at any time.'
        : 'They can sign in again, and their waiting messages go out.',
      acceptLabel: off ? 'Turn off' : 'Turn on',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: off ? 'danger' : 'primary' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => void this.run(() => this.api.setActive(m.id, !off), off ? 'Turned off' : 'Turned on'),
    });
  }

  protected openReset(): void {
    this.resetPasswordValue = '';
    this.resetOpen.set(true);
  }

  protected async saveReset(m: Member): Promise<void> {
    const ok = await this.run(() => this.api.resetPassword(m.id, this.resetPasswordValue), 'New password set');
    if (ok) this.resetOpen.set(false);
  }

  protected remove(m: Member): void {
    this.confirm.confirm({
      header: `Delete ${m.displayName}?`,
      message:
        'The account is removed for good, with all its contacts, groups, templates and messages, and its WhatsApp number is ' +
        'logged out. This cannot be undone. To only stop them, turn them off instead.',
      acceptLabel: 'Delete for good',
      rejectLabel: 'Cancel',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        void this.run(async () => {
          await this.api.remove(m.id);
          this.selectedId.set(null);
        }, 'Deleted'),
    });
  }

  protected openAdd(): void {
    this.add = { userName: '', displayName: '', password: '', isAdmin: false, switches: [] };
    this.addError.set(null);
    this.addOpen.set(true);
  }

  protected toggleAddSwitch(permission: string): void {
    const s = this.add.switches;
    this.add.switches = s.includes(permission) ? s.filter((p) => p !== permission) : [...s, permission];
  }

  protected async saveAdd(): Promise<void> {
    this.addError.set(null);
    const value = { ...this.add, userName: this.add.userName.trim(), displayName: this.add.displayName.trim() };
    if (!/^[a-zA-Z0-9._-]{1,50}$/.test(value.userName) || !value.displayName) {
      this.addError.set('Fill in a user name (letters, digits, ".", "_" or "-") and a name.');
      return;
    }

    const min = value.isAdmin ? 12 : 6;
    if (value.password.length < min) {
      this.addError.set(`The first password needs at least ${min} characters.`);
      return;
    }

    this.saving.set(true);
    try {
      const { id } = await this.api.create({ ...value, switches: value.isAdmin ? [] : value.switches });
      this.addOpen.set(false);
      this.toast.add({ severity: 'success', summary: 'Added', detail: `${value.displayName} chooses their own password at the first sign-in.` });
      await this.load();
      this.selectedId.set(id);
      this.editName = value.displayName;
    } catch (e) {
      this.addError.set(problemMessage(e));
    } finally {
      this.saving.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      this.members.set(await this.api.list());
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not load', detail: problemMessage(e), life: 6000 });
    } finally {
      this.loading.set(false);
    }
  }

  /** Saves, then reloads; shows the server's message if something is refused. */
  private async run(action: () => Promise<unknown>, done: string | null): Promise<boolean> {
    this.saving.set(true);
    try {
      await action();
      if (done) this.toast.add({ severity: 'success', summary: done });
      await this.load();
      return true;
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not saved', detail: problemMessage(e), life: 6000 });
      await this.load();
      return false;
    } finally {
      this.saving.set(false);
    }
  }
}
