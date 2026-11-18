import { Component, computed, DestroyRef, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MessageService } from 'primeng/api';
import { filter } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { WhatsAppLiveService } from '../../core/realtime/whatsapp-live.service';
import { moduleIcon, ModulesStore } from '../../features/modules/modules.api';
import { NotificationsStore } from '../../features/notifications/notifications.api';
import { SendingBar } from './sending-bar';

interface Tab {
  label: string;
  icon: string;
  link: string;
  /** Shown when the user has any one of these (none = everyone). */
  permissions?: string[];
  badge?: number;
}

/** Occasions shown one by one in the side rail; the rest are under "More". */
const RAIL_MODULES = 3;

/**
 * The frame around every signed-in page, built like a chat app: tabs at the bottom on a phone, a slim rail on the
 * left on a computer and a tablet. Inside an open chat (or the new-message screen) the phone tabs hide, as in WhatsApp.
 * Occasions (Birthday, Anniversary, ...) each get their own item in the rail; on a phone they are under "Occasions".
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
  private readonly live = inject(WhatsAppLiveService);
  private readonly toast = inject(MessageService);
  private readonly modules = inject(ModulesStore);
  protected readonly notifications = inject(NotificationsStore);
  private readonly url = signal(this.router.url);

  private can(p: string): boolean {
    return this.auth.can(p);
  }

  /** Side rail (computer, tablet). */
  protected readonly rail = computed<Tab[]>(() => {
    this.auth.user(); // re-evaluate when the user changes
    const tabs: Tab[] = [];
    if (this.can('platform.tenants')) {
      tabs.push({ label: 'Dashboard', icon: 'pi pi-chart-bar', link: '/platform/dashboard' });
      tabs.push({ label: 'Tenants', icon: 'pi pi-building', link: '/platform/tenants' });
    }
    if (this.can('messages.send')) tabs.push({ label: 'Chats', icon: 'pi pi-comments', link: '/chats' });
    if (this.can('contacts.view')) tabs.push({ label: 'Contacts', icon: 'pi pi-address-book', link: '/contacts' });
    if (this.can('modules.manage')) {
      const list = this.modules.modules();
      for (const m of list.slice(0, RAIL_MODULES)) {
        tabs.push({ label: m.name, icon: moduleIcon(m), link: `/modules/${m.id}`, badge: m.todayCount || undefined });
      }
      tabs.push({ label: list.length > RAIL_MODULES ? 'More' : 'Occasions', icon: list.length > RAIL_MODULES ? 'pi pi-ellipsis-h' : 'pi pi-calendar-plus', link: '/modules' });
    }
    if (this.can('messages.send')) {
      tabs.push({ label: 'Sending', icon: 'pi pi-send', link: '/sending' });
      tabs.push({ label: 'Alerts', icon: 'pi pi-bell', link: '/notifications', badge: this.notifications.unread() || undefined });
    }
    tabs.push({ label: 'Me', icon: 'pi pi-user', link: '/me' });
    return tabs;
  });

  /** Bottom tabs (phone): at most five. */
  protected readonly tabbar = computed<Tab[]>(() => {
    this.auth.user();
    const tabs: Tab[] = [];
    if (this.can('platform.tenants')) {
      tabs.push({ label: 'Dashboard', icon: 'pi pi-chart-bar', link: '/platform/dashboard' });
      tabs.push({ label: 'Tenants', icon: 'pi pi-building', link: '/platform/tenants' });
    }
    if (this.can('messages.send')) tabs.push({ label: 'Chats', icon: 'pi pi-comments', link: '/chats' });
    if (this.can('contacts.view')) tabs.push({ label: 'Contacts', icon: 'pi pi-address-book', link: '/contacts' });
    if (this.can('modules.manage')) {
      tabs.push({ label: 'Occasions', icon: 'pi pi-calendar', link: '/modules', badge: this.modules.todayTotal() || undefined });
    }
    if (this.can('messages.send')) tabs.push({ label: 'Sending', icon: 'pi pi-send', link: '/sending' });
    tabs.push({ label: 'Me', icon: 'pi pi-user', link: '/me', badge: this.notifications.unread() || undefined });
    return tabs.slice(-5);
  });

  /** The global sending bar is for users who send messages. */
  protected readonly canSend = computed(() => {
    this.auth.user();
    return this.auth.can('messages.send');
  });

  /** In a chat or composing: the screen gets the whole height on a phone. */
  protected readonly inConversation = computed(() => /^\/chats\/.+/.test(this.url()));

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(destroyRef),
      )
      .subscribe((e) => this.url.set((e as NavigationEnd).urlAfterRedirects));

    // Menu data and live notifications for whoever is signed in; cleared on sign-out.
    let started = false;
    effect(() => {
      const user = this.auth.user();
      // Runs again only when the signed-in user changes, not when the menu or the bell refresh.
      untracked(() => {
        if (!user) {
          this.modules.clear();
          this.notifications.clear();
          return;
        }

        if (this.can('modules.manage')) void this.modules.reload();
        if (this.can('messages.send')) {
          void this.notifications.reload();
          if (!started) {
            started = true;
            void this.live.start();
          }
        }
      });
    });
    destroyRef.onDestroy(() => {
      if (started) void this.live.stop();
    });

    this.live.notification$.pipe(takeUntilDestroyed(destroyRef)).subscribe((n) => {
      this.notifications.add(n);
      this.toast.add({ severity: n.kind === 'occasion' ? 'success' : 'warn', summary: n.title, detail: n.body ?? undefined, life: 8000 });
      if (n.kind === 'occasion') void this.modules.reload();
    });
  }
}
