import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { KIND_LABELS } from '../../core/auth/auth.models';
import { applyThemeMode, savedThemeMode, ThemeMode } from '../../core/config/theme';
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
  /** A part of the page to scroll to. */
  fragment?: string;
  permissions: string[];
}

const ITEMS: Item[] = [
  // The same list, in the same order, as the app's Me screen. Occasions and Sending are main tabs, not here.
  { label: 'Message one person', hint: 'Type a message to one number', icon: 'pi pi-comment', link: '/whatsapp', fragment: 'message-one', permissions: ['messages.send'] },
  { label: 'Notifications', hint: 'Your number, sending and occasions', icon: 'pi pi-bell', link: '/notifications', permissions: ['messages.send'] },
  { label: 'Message templates', hint: 'Your saved messages', icon: 'pi pi-bookmark', link: '/templates', permissions: ['templates.manage'] },
  { label: 'Categories and groups', hint: 'Family, friends, elders…', icon: 'pi pi-tags', link: '/contacts/lists', permissions: ['contacts.view'] },
  { label: 'Import contacts', hint: 'From an Excel or CSV file', icon: 'pi pi-file-import', link: '/contacts/import', permissions: ['contacts.import'] },
  { label: 'Sending overview', hint: 'Recent messages and ticks', icon: 'pi pi-chart-bar', link: '/home', permissions: ['messages.send'] },
];

const ADMIN: Item[] = [
  { label: 'Members', hint: 'Add people and choose what they may do', icon: 'pi pi-users', link: '/members', permissions: ['members.manage'] },
  { label: 'Dashboard', hint: 'All organisations at a glance', icon: 'pi pi-chart-bar', link: '/platform/dashboard', permissions: ['platform.tenants'] },
  { label: 'Tenants', hint: 'Customers and their admins', icon: 'pi pi-building', link: '/platform/tenants', permissions: ['platform.tenants'] },
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
  protected readonly theme = signal<ThemeMode>(savedThemeMode());
  protected readonly themes: { id: ThemeMode; label: string }[] = [
    { id: 'light', label: 'Light' },
    { id: 'dark', label: 'Dark' },
    { id: 'system', label: 'Device' },
  ];

  protected setTheme(mode: ThemeMode): void {
    this.theme.set(mode);
    applyThemeMode(mode);
  }
  protected readonly status = computed(() => connectionStatus(this.account()?.status));
  protected readonly initials = computed(() => initialsOf(this.auth.user()?.displayName ?? ''));
  protected readonly canWhatsApp = computed(() => {
    this.auth.user();
    return this.auth.can('whatsapp.link') || this.auth.can('messages.send');
  });
  protected readonly items = computed(() => this.allowed(ITEMS));
  /** "Admin · Bhakti Enterprise", "Member · …" or "Platform admin". */
  protected readonly who = computed(() => {
    const user = this.auth.user();
    return user ? [KIND_LABELS[user.kind], user.tenantName].filter(Boolean).join(' · ') : '';
  });
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
