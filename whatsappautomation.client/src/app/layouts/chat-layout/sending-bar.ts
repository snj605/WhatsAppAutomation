import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../../features/broadcasts/broadcasts.api';
import { Queue, QueueItem, SendingApi } from '../../features/sending/sending.api';

const REFRESH_MS = 6000;

/**
 * The strip at the top of every screen that shows what is being sent right now. When nothing is queued it is
 * dimmed and cannot be used; while a broadcast is sending (or paused) it lights up with the progress, Pause or
 * Continue, and Stop, and tapping it opens that chat.
 */
@Component({
  selector: 'app-sending-bar',
  imports: [RouterLink],
  templateUrl: './sending-bar.html',
  styleUrl: './sending-bar.css',
})
export class SendingBar implements OnInit {
  private readonly api = inject(BroadcastsApi);
  private readonly confirm = inject(ConfirmationService);
  private readonly toast = inject(MessageService);

  private readonly sending = inject(SendingApi);
  protected readonly queue = signal<QueueItem[]>([]);
  protected readonly pacing = signal<Queue | null>(null);
  protected readonly busy = signal(false);

  /** The broadcast shown: the oldest one still in the queue (that is the one being sent). */
  protected readonly current = computed(() => this.queue()[0] ?? null);
  protected readonly more = computed(() => Math.max(0, this.queue().length - 1));
  protected readonly active = computed(() => this.current() !== null);
  protected readonly sent = computed(() => this.current()?.counts.sent ?? 0);
  protected readonly willGet = computed(() => {
    const c = this.current()?.counts;
    return c ? c.total - c.skipped : 0;
  });
  protected readonly percent = computed(() => (this.willGet() === 0 ? 0 : Math.round((this.sent() / this.willGet()) * 100)));
  protected readonly note = computed(() => {
    const b = this.current();
    if (!b) return 'Nothing is being sent';
    if (b.status === 'paused') return b.pauseReason ?? 'Paused';
    const p = this.pacing();
    if (p?.waiting) return p.waiting;
    if (p?.nextSendAt) return `Next at ${new Date(p.nextSendAt).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: true })}`;
    return 'Sending one by one';
  });

  constructor() {
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible') void this.load();
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void {
    void this.load();
  }

  protected pause(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    void this.run('pause');
  }

  protected resume(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    void this.run('resume');
  }

  protected stop(event: Event): void {
    event.preventDefault();
    event.stopPropagation();
    const b = this.current();
    if (!b) return;
    this.confirm.confirm({
      header: 'Stop sending?',
      message: `"${b.name}": messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Stop sending',
      rejectLabel: 'Keep sending',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () => void this.run('cancel'),
    });
  }

  private async run(action: 'pause' | 'resume' | 'cancel'): Promise<void> {
    const b = this.current();
    if (!b) return;
    this.busy.set(true);
    try {
      await this.api.change(b.id, action);
      await this.load();
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not changed', detail: problemMessage(e), life: 6000 });
    } finally {
      this.busy.set(false);
    }
  }

  private async load(): Promise<void> {
    try {
      // One call: the queue in sending order (occasions first, then the order the user set) with the pace.
      const queue = await this.sending.queue();
      this.queue.set(queue.items);
      this.pacing.set(queue);
    } catch {
      // Keep what is shown; the next refresh tries again.
    }
  }
}
