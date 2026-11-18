import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface AppNotification {
  id: string;
  kind: 'whatsapp' | 'paused' | 'occasion' | string;
  title: string;
  body: string | null;
  createdAt: string;
  read: boolean;
}

/** The user's notifications and unread count, for the bell and the list (new ones arrive live). */
@Injectable({ providedIn: 'root' })
export class NotificationsStore {
  private readonly http = inject(HttpClient);
  readonly items = signal<AppNotification[]>([]);
  readonly unread = signal(0);

  async reload(): Promise<void> {
    try {
      const page = await firstValueFrom(this.http.get<{ items: AppNotification[]; unread: number }>('/api/v1/notifications'));
      this.items.set(page.items);
      this.unread.set(page.unread);
    } catch {
      // The bell keeps its last state.
    }
  }

  add(n: AppNotification): void {
    this.items.set([n, ...this.items()].slice(0, 50));
    this.unread.set(this.unread() + (n.read ? 0 : 1));
  }

  async markAllRead(): Promise<void> {
    if (this.unread() === 0) return;
    await firstValueFrom(this.http.post<void>('/api/v1/notifications/read', { ids: null }));
    this.items.set(this.items().map((n) => ({ ...n, read: true })));
    this.unread.set(0);
  }

  clear(): void {
    this.items.set([]);
    this.unread.set(0);
  }
}
