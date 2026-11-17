import { DatePipe } from '@angular/common';
import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ButtonModule } from 'primeng/button';
import { SkeletonModule } from 'primeng/skeleton';
import { AuthService } from '../../core/auth/auth.service';
import { ContactsApi } from '../contacts/contacts.api';
import { UpcomingBirthday } from '../contacts/contacts.models';
import { WhatsAppApi } from '../whatsapp/whatsapp.api';
import { OutgoingMessage, WhatsAppAccount } from '../whatsapp/whatsapp.models';
import { connectionStatus, messageTick } from '../whatsapp/whatsapp.status';

interface QuickAction {
  label: string;
  text: string;
  icon: string;
  tone: string;
  link: string;
  permissions: string[];
}

const ACTIONS: QuickAction[] = [
  {
    label: 'WhatsApp',
    text: 'Link your number and send a test message',
    icon: 'pi pi-whatsapp',
    tone: '',
    link: '/whatsapp',
    permissions: ['whatsapp.link', 'messages.send'],
  },
  { label: 'Members', text: 'Add people and choose what they may do', icon: 'pi pi-users', tone: 'blue', link: '/members', permissions: ['members.manage'] },
  { label: 'Tenants', text: 'Customers and their admins', icon: 'pi pi-building', tone: 'violet', link: '/platform/tenants', permissions: ['platform.tenants'] },
];

/** Home: a greeting, the WhatsApp status, recent message results and shortcuts. */
@Component({
  selector: 'app-home-page',
  imports: [DatePipe, RouterLink, ButtonModule, SkeletonModule],
  templateUrl: './home.page.html',
  styleUrl: './home.page.css',
})
export class HomePage implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(WhatsAppApi);
  private readonly contactsApi = inject(ContactsApi);
  private readonly router = inject(Router);

  /** Today's birthdays, as a reminder. */
  protected readonly birthdaysToday = signal<UpcomingBirthday[]>([]);
  protected readonly canBroadcast = computed(() => {
    this.auth.user();
    return this.auth.can('modules.manage');
  });

  /** Today's birthdays are one chat with a checklist of who got wishes. */
  protected sendBirthdayWishes(): void {
    void this.router.navigate(['/chats/birthdays']);
  }

  protected readonly loading = signal(true);
  protected readonly account = signal<WhatsAppAccount | null>(null);
  protected readonly messages = signal<OutgoingMessage[]>([]);

  protected readonly canWhatsApp = computed(() => {
    this.auth.user();
    return this.auth.can('whatsapp.link') || this.auth.can('messages.send');
  });

  protected readonly status = computed(() => connectionStatus(this.account()?.status));

  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    return hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
  });

  protected readonly actions = computed(() => {
    this.auth.user();
    return ACTIONS.filter((a) => a.permissions.some((p) => this.auth.can(p)));
  });

  /** Counts over the recent messages the server returns (the latest 20). */
  protected readonly stats = computed(() => {
    const list = this.messages();
    const count = (pred: (m: OutgoingMessage) => boolean) => list.filter(pred).length;
    return {
      total: list.length,
      delivered: count((m) => m.status === 'delivered' || m.status === 'read'),
      read: count((m) => m.status === 'read'),
      failed: count((m) => m.status === 'failed'),
    };
  });

  protected readonly tick = messageTick;

  async ngOnInit(): Promise<void> {
    if (this.auth.can('contacts.view')) {
      this.contactsApi
        .birthdays(0)
        .then((list) => this.birthdaysToday.set(list))
        .catch(() => undefined); // the reminder is optional; the Birthdays page shows errors
    }

    if (!this.canWhatsApp()) {
      this.loading.set(false);
      return;
    }

    try {
      this.account.set(await this.api.account());
      if (this.auth.can('messages.send')) {
        this.messages.set(await this.api.messages());
      }
    } catch {
      // Home stays usable without these; the WhatsApp page shows the error in full.
    } finally {
      this.loading.set(false);
    }
  }
}
