import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../broadcasts/broadcasts.api';
import { BroadcastDetails, Pacing, Recipient, RECIPIENT_STATUS, RecipientStatus } from '../broadcasts/broadcasts.models';
import { highlightPlaceholders } from '../broadcasts/placeholders';
import { fileIcon, fileSize } from '../files/files.api';
import { avatarColors, initialsOf } from './chat-format';

type Filter = 'all' | 'waiting' | 'sent' | 'problems';

/**
 * One broadcast shown as a chat: the message as a green bubble, where sending stands as small grey notes, and
 * "who got it" with ticks per person. Pause, continue or stop from the top bar.
 */
@Component({
  selector: 'app-chat-page',
  imports: [DatePipe, RouterLink],
  templateUrl: './chat.page.html',
  styleUrl: './chat.page.css',
})
export class ChatPage {
  private readonly api = inject(BroadcastsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  /** From the route (:id), through withComponentInputBinding. */
  readonly id = input.required<string>();

  protected readonly details = signal<BroadcastDetails | null>(null);
  protected readonly pacing = signal<Pacing | null>(null);
  protected readonly busy = signal(false);
  protected readonly showPeople = signal(false);
  protected readonly filter = signal<Filter>('all');
  protected readonly initials = initialsOf;
  protected readonly colors = avatarColors;
  protected readonly recipientStatus = RECIPIENT_STATUS;
  protected readonly fileIcon = fileIcon;
  protected readonly fileSize = fileSize;

  protected readonly summary = computed(() => this.details()?.summary ?? null);

  /** Share of the people who will get it that already have it, 0–100. */
  protected readonly percent = computed(() => {
    const c = this.summary()?.counts;
    if (!c) return 0;
    const willGet = c.total - c.skipped;
    return willGet <= 0 ? 0 : Math.round((c.sent / willGet) * 100);
  });

  /** One plain sentence: where sending stands and what happens next. */
  protected readonly statusLine = computed(() => {
    const s = this.summary();
    if (!s) return '';
    const c = s.counts;
    const willGet = c.total - c.skipped;
    switch (s.status) {
      case 'sending': {
        const p = this.pacing();
        const next = p?.waiting
          ? p.waiting
          : p?.nextSendAt
            ? `Next one at ${new Date(p.nextSendAt).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true })}.`
            : '';
        return `Sending: ${c.sent} of ${willGet} sent. ${next}`.trim();
      }
      case 'paused':
        return `Paused after ${c.sent} of ${willGet}. ${s.pauseReason ?? 'Press Continue to go on.'}`;
      case 'completed':
        return `Done: all ${c.sent} sent${c.failed > 0 ? `, ${c.failed} could not be sent` : ''}.`;
      default:
        return `Stopped: ${c.sent} sent, the rest will not get it.`;
    }
  });

  /** The bubbles: the one message, or one per language for older broadcasts. */
  protected readonly bubbles = computed(() => {
    const d = this.details();
    if (!d) return [];
    if (d.text) return [{ label: null as string | null, html: highlightPlaceholders(d.text) }];
    return [
      { label: 'English', text: d.textEn },
      { label: 'Hindi', text: d.textHi },
      { label: 'Gujarati', text: d.textGu },
    ]
      .filter((t) => t.text)
      .map((t) => ({ label: t.label as string | null, html: highlightPlaceholders(t.text!) }));
  });

  /** One real example of what a person received. */
  protected readonly example = computed(() => this.details()?.recipients.find((r) => r.text && r.status !== 'skipped') ?? null);

  protected readonly counts = computed(() => {
    const c = this.summary()?.counts;
    if (!c) return null;
    return { sent: c.sent, delivered: c.delivered, read: c.read, waiting: c.pending, failed: c.failed, skipped: c.skipped, total: c.total };
  });

  protected readonly people = computed(() => {
    const all = this.details()?.recipients ?? [];
    const pick: Record<Filter, (s: RecipientStatus) => boolean> = {
      all: () => true,
      waiting: (s) => s === 'pending',
      sent: (s) => s === 'sent' || s === 'delivered' || s === 'read',
      problems: (s) => s === 'failed' || s === 'skipped',
    };
    return all.filter((r) => pick[this.filter()](r.status));
  });

  constructor() {
    effect(() => {
      this.id(); // load again when another chat is opened in the same screen
      this.details.set(null);
      void this.load(true);
    });

    const timer = setInterval(() => {
      const s = this.summary()?.status;
      if (s === 'sending' || s === 'paused') void this.load(false);
    }, 5000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected tick(r: Recipient) {
    return RECIPIENT_STATUS[r.status];
  }

  protected pause(): void {
    void this.run('pause');
  }

  protected resume(): void {
    void this.run('resume');
  }

  protected stop(): void {
    this.confirm.confirm({
      header: 'Stop sending?',
      message: 'Messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Stop sending',
      rejectLabel: 'Keep sending',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => void this.run('cancel'),
    });
  }

  private async run(action: 'pause' | 'resume' | 'cancel'): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.change(this.id(), action);
      await this.load(true);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not changed', detail: problemMessage(e), life: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  private async load(showErrors: boolean): Promise<void> {
    const id = this.id();
    try {
      const [details, pacing] = await Promise.all([this.api.get(id), this.api.pacing()]);
      if (id !== this.id()) return; // another chat was opened meanwhile
      this.details.set(details);
      this.pacing.set(pacing);
    } catch (e) {
      if (showErrors) this.toast.add({ severity: 'error', summary: 'Could not load the chat', detail: problemMessage(e), life: 6000 });
    }
  }
}
