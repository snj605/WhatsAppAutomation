import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { avatarColors, chatSubtitle, initialsOf } from './chat-format';
import { ChatKind, chatLink, ChatListItem, ChatsApi } from './chats.api';

type ListFilter = 'all' | 'group' | 'category' | 'other';

/**
 * Chats, like WhatsApp: every group and category is a chat you can message straight away, today's birthdays are
 * one chat with a checklist, and sends to picked people are one-off chats that leave the list 5 days after they
 * finish. On a computer the open chat shows on the right; on a phone it takes the whole screen.
 */
@Component({
  selector: 'app-chats-shell',
  imports: [DatePipe, RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './chats-shell.html',
  styleUrl: './chats-shell.css',
})
export class ChatsShell implements OnInit {
  private readonly api = inject(ChatsApi);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly chats = signal<ChatListItem[]>([]);
  protected readonly search = signal('');
  protected readonly filter = signal<ListFilter>('all');
  private readonly url = signal(this.router.url);

  /** A chat or the new-message screen is open (on a phone the list then hides). */
  protected readonly childOpen = computed(() => /^\/chats\/.+/.test(this.url()));
  protected readonly counts = computed(() => {
    const list = this.chats();
    return {
      group: list.filter((c) => c.kind === 'group').length,
      category: list.filter((c) => c.kind === 'category').length,
      other: list.filter((c) => c.kind === 'broadcast' || c.kind === 'birthdays').length,
    };
  });
  protected readonly shown = computed(() => {
    const q = this.search().trim().toLowerCase();
    const f = this.filter();
    return this.chats().filter(
      (c) =>
        (f === 'all' || (f === 'other' ? c.kind === 'broadcast' || c.kind === 'birthdays' : c.kind === f)) &&
        (!q || c.name.toLowerCase().includes(q)),
    );
  });

  protected readonly initials = initialsOf;
  protected readonly colors = avatarColors;
  protected readonly link = chatLink;

  constructor() {
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe((e) => {
        this.url.set((e as NavigationEnd).urlAfterRedirects);
        void this.load(); // a new message or a renamed group shows up in the list
      });

    // Keep progress fresh while something is sending.
    const timer = setInterval(() => {
      if (this.chats().some((c) => c.last?.status === 'sending')) void this.load();
    }, 10000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.load();
  }

  protected setFilter(f: ListFilter): void {
    this.filter.set(f);
  }

  /** The grey line under a chat's name. */
  protected subtitle(c: ChatListItem): string {
    if (c.last && (c.last.status === 'sending' || c.last.status === 'paused')) return chatSubtitle(c.last);
    if (c.kind === 'birthdays') return c.memberCount === 1 ? '1 birthday today · tap to wish' : `${c.memberCount} birthdays today · tap to wish`;
    if (c.lastText) return c.lastText.replace(/\s+/g, ' ');
    if (c.kind === 'group' || c.kind === 'category') return `${c.memberCount} ${c.memberCount === 1 ? 'member' : 'members'} · tap to send a message`;
    return c.last ? chatSubtitle(c.last) : '';
  }

  protected kindLabel(kind: ChatKind): string {
    return kind === 'group' ? 'Group' : kind === 'category' ? 'Category' : kind === 'birthdays' ? 'Birthdays' : 'Sent list';
  }

  private async load(): Promise<void> {
    try {
      this.chats.set(await this.api.list());
    } catch {
      // The list keeps what it had; the open chat shows errors in full.
    } finally {
      this.loading.set(false);
    }
  }
}
