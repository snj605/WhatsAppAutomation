import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { filter, map } from 'rxjs';
import { waClock } from '../../shared/wa-preview/wa-format';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../../features/broadcasts/broadcasts.api';
import { Queue, QueueItem, SendingApi } from '../../features/sending/sending.api';

const REFRESH_MS = 6000;

/**
 * The slim strip at the top of every screen while something is sending, paused or waiting: what, how many of how
 * many, the next time or why it waits, Pause or Continue and Stop. Tapping it opens the full queue (Sending). It
 * is not shown when nothing is queued, nor on the Sending page itself, which has the full view.
 */
@Component({
  selector: 'app-sending-bar',
  imports: [RouterLink],
  host: { '[class.visible]': 'shown()' },
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
  private readonly router = inject(Router);
  private readonly url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map(() => this.router.url)),
    { initialValue: this.router.url },
  );
  /** Shown while something is queued, except on the Sending page (it has the full view). */
  protected readonly shown = computed(() => this.active() && !this.url().startsWith('/sending'));
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
    if (p && !p.connected) return 'WhatsApp is not connected';
    if (p?.nextSendAt) {
      const next = new Date(p.nextSendAt);
      return next.getTime() <= Date.now() + 5000 ? 'Sending now' : `Next at ${waClock(next)}`;
    }
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
