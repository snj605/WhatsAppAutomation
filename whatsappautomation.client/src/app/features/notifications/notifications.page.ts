import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { whenText } from '../modules/time-text';
import { AppNotification, NotificationsStore } from './notifications.api';

/** The user's notifications, newest first. Opening the page marks them read. */
@Component({
  selector: 'app-notifications-page',
  imports: [RouterLink],
  template: `
    <div class="ap-page">
      <header class="ap-header">
        <div class="ap-header-row">
          <div>
            <h1 class="ap-title">Notifications</h1>
            <p class="ap-sub">Your number, sending and occasion wishes.</p>
          </div>
        </div>
      </header>
      <div class="ap-body">
        @if (store.items().length === 0) {
          <section class="ap-card ap-empty">
            <i class="pi pi-bell" aria-hidden="true"></i>
            <strong>Nothing yet</strong>
            <span>You hear here when your number disconnects, sending pauses or occasion wishes go out.</span>
          </section>
        } @else {
          <section class="ap-card">
            @for (n of store.items(); track n.id) {
              <a class="ap-row link" [routerLink]="linkOf(n)" [class.unread]="!n.read">
                <span class="ap-icon" aria-hidden="true"><i [class]="iconOf(n)"></i></span>
                <span class="ap-text">
                  <strong>{{ n.title }}</strong>
                  @if (n.body) {<small>{{ n.body }}</small>}
                </span>
                <small class="when">{{ whenText(n.createdAt) }}</small>
              </a>
            }
          </section>
        }
      </div>
    </div>
  `,
  styleUrls: ['../../shared/approved.css'],
  styles: `
    .link { text-decoration: none; color: inherit; }
    .link:hover { background: var(--ap-line); }
    .unread { background: var(--ap-accent-soft); }
    .when { font-size: 12px; color: var(--ap-muted); white-space: nowrap; }
  `,
})
export class NotificationsPage implements OnInit {
  protected readonly store = inject(NotificationsStore);
  protected readonly whenText = whenText;

  async ngOnInit(): Promise<void> {
    await this.store.reload();
    await this.store.markAllRead().catch(() => undefined);
  }

  protected iconOf(n: AppNotification): string {
    return n.kind === 'whatsapp' ? 'pi pi-mobile' : n.kind === 'paused' ? 'pi pi-pause' : n.kind === 'occasion' ? 'pi pi-gift' : 'pi pi-bell';
  }

  protected linkOf(n: AppNotification): string {
    return n.kind === 'whatsapp' ? '/whatsapp' : n.kind === 'occasion' ? '/modules' : '/sending';
  }
}
