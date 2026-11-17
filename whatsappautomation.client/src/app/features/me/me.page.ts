import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { initialsOf } from '../chats/chat-format';
import { WhatsAppApi } from '../whatsapp/whatsapp.api';
import { WhatsAppAccount } from '../whatsapp/whatsapp.models';
import { connectionStatus } from '../whatsapp/whatsapp.status';

interface Item {
  label: string;
  hint: string;
  icon: string;
  link: string;
  permissions: string[];
}

const ITEMS: Item[] = [
  { label: 'Message templates', hint: 'Your saved messages', icon: 'pi pi-bookmark', link: '/templates', permissions: ['broadcasts.send'] },
  { label: 'Import contacts', hint: 'From an Excel or CSV file', icon: 'pi pi-file-import', link: '/contacts/import', permissions: ['contacts.manage'] },
  { label: 'Categories and groups', hint: 'Family, friends, elders…', icon: 'pi pi-tags', link: '/contacts/lists', permissions: ['contacts.view'] },
  { label: 'Sending overview', hint: 'Recent messages and ticks', icon: 'pi pi-chart-bar', link: '/home', permissions: ['messages.send'] },
];

const ADMIN: Item[] = [
  { label: 'Users', hint: 'Create accounts and give roles', icon: 'pi pi-users', link: '/settings/users', permissions: ['users.view'] },
  { label: 'Roles', hint: 'What each role may do', icon: 'pi pi-shield', link: '/settings/roles', permissions: ['roles.view'] },
];

/** Me: your WhatsApp number, tools, admin pages (only for admins) and sign out. */
@Component({
  selector: 'app-me-page',
  imports: [RouterLink],
  templateUrl: './me.page.html',
  styleUrl: './me.page.css',
})
export class MePage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly whatsapp = inject(WhatsAppApi);

  protected readonly account = signal<WhatsAppAccount | null>(null);
  protected readonly status = computed(() => connectionStatus(this.account()?.status));
  protected readonly initials = computed(() => initialsOf(this.auth.user()?.displayName ?? ''));
  protected readonly canWhatsApp = computed(() => {
    this.auth.user();
    return this.auth.can('whatsapp.link') || this.auth.can('messages.send');
  });
  protected readonly items = computed(() => this.allowed(ITEMS));
  protected readonly admin = computed(() => this.allowed(ADMIN));

  async ngOnInit(): Promise<void> {
    if (!this.canWhatsApp()) return;
    try {
      this.account.set(await this.whatsapp.account());
    } catch {
      // The WhatsApp page shows errors in full.
    }
  }

  protected signOut(): void {
    void this.auth.signOut();
  }

  private allowed(items: Item[]): Item[] {
    this.auth.user();
    return items.filter((i) => i.permissions.some((p) => this.auth.can(p)));
  }
}
