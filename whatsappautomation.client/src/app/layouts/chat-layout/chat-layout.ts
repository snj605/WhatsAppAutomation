import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { SendingBar } from './sending-bar';

interface Tab {
  label: string;
  icon: string;
  link: string;
  /** Shown when the user has any one of these (none = everyone). */
  permissions?: string[];
}

const TABS: Tab[] = [
  { label: 'Tenants', icon: 'pi pi-building', link: '/platform/tenants', permissions: ['platform.tenants'] },
  { label: 'Chats', icon: 'pi pi-comments', link: '/chats', permissions: ['messages.send'] },
  { label: 'Contacts', icon: 'pi pi-address-book', link: '/contacts', permissions: ['contacts.view'] },
  { label: 'Birthdays', icon: 'pi pi-gift', link: '/birthdays', permissions: ['contacts.view'] },
  { label: 'Me', icon: 'pi pi-user', link: '/me' },
];

/**
 * The frame around every signed-in page, built like a chat app: tabs at the bottom on a phone, a slim rail on the
 * left on a computer. Inside an open chat (or the new-message screen) the phone tabs hide, as in WhatsApp.
 */
@Component({
  selector: 'app-chat-layout',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, SendingBar],
  templateUrl: './chat-layout.html',
  styleUrl: './chat-layout.css',
})
export class ChatLayout {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly url = signal(this.router.url);

  protected readonly tabs = computed(() => {
    this.auth.user(); // re-evaluate when the user changes
    return TABS.filter((t) => !t.permissions || t.permissions.some((p) => this.auth.can(p)));
  });

  /** The global sending bar is for users who send messages. */
  protected readonly canSend = computed(() => {
    this.auth.user();
    return this.auth.can('messages.send');
  });

  /** In a chat or composing: the screen gets the whole height on a phone. */
  protected readonly inConversation = computed(() => /^\/chats\/.+/.test(this.url()));

  constructor() {
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((e) => this.url.set((e as NavigationEnd).urlAfterRedirects));
  }
}
