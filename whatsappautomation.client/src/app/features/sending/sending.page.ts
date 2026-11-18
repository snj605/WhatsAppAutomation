import { Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { SkeletonModule } from 'primeng/skeleton';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../broadcasts/broadcasts.api';
import { clockText, whenText } from '../modules/time-text';
import { Queue, QueueItem, SendingApi, SendingSettings } from './sending.api';

type Tab = 'queue' | 'settings';
const REFRESH_MS = 15_000;

/**
 * Sending, the same as on the phone:
 * - Today's queue: what is waiting, in the order it goes out (occasions first), with when each should start and
 *   finish; move items up or down, pause, continue or stop; open the results.
 * - Settings: messages a day (300–500), how many in one go and the rest after, the pause between messages and the
 *   hours to send in. All chosen by the user.
 */
@Component({
  selector: 'app-sending-page',
  imports: [FormsModule, RouterLink, SkeletonModule],
  templateUrl: './sending.page.html',
  styleUrls: ['../../shared/approved.css', './sending.page.css'],
})
export class SendingPage implements OnInit {
  private readonly api = inject(SendingApi);
  private readonly broadcasts = inject(BroadcastsApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  protected readonly whenText = whenText;
  protected readonly clockText = clockText;

  protected readonly tab = signal<Tab>('queue');
  protected readonly queue = signal<Queue | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly busy = signal<string | null>(null);

  protected readonly settings = signal<SendingSettings | null>(null);
  protected readonly saving = signal(false);
  protected form = { dailyLimit: 300, chunkSize: 50, chunkRestMinutes: 15, sendFrom: '09:00', sendUntil: '21:00', minGapSeconds: 30, maxGapSeconds: 90, anyTime: false };

  protected readonly usedPercent = computed(() => {
    const q = this.queue();
    return q && q.dailyLimit > 0 ? Math.min(100, Math.round((q.sentToday / q.dailyLimit) * 100)) : 0;
  });

  /** About how many fit in a day with what is on the form (the server gives the exact number after saving). */
  protected fitsPerDay(): number {
    const f = this.form;
    const hours = f.anyTime ? 24 : windowHours(f.sendFrom, f.sendUntil);
    const gap = (Number(f.minGapSeconds) + Number(f.maxGapSeconds)) / 2;
    const chunk = Math.max(1, Number(f.chunkSize));
    const perChunkSeconds = chunk * gap + Number(f.chunkRestMinutes) * 60;
    const byTime = Math.floor(((hours * 3600) / perChunkSeconds) * chunk);
    return Math.max(0, Math.min(Number(f.dailyLimit), byTime));
  }

  constructor() {
    const timer = setInterval(() => {
      if (document.visibilityState === 'visible' && this.tab() === 'queue') void this.loadQueue(false);
    }, REFRESH_MS);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.loadQueue(true);
  }

  protected async setTab(t: Tab): Promise<void> {
    this.tab.set(t);
    if (t === 'settings' && !this.settings()) {
      try {
        this.useSettings(await this.api.settings());
      } catch (e) {
        this.showError('Could not load the settings', e);
      }
    }
  }

  protected async loadQueue(first: boolean): Promise<void> {
    if (first) this.loading.set(true);
    try {
      this.queue.set(await this.api.queue());
      this.error.set(null);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected progress(item: QueueItem): number {
    const willGet = item.counts.total - item.counts.skipped;
    const done = item.counts.sent + item.counts.failed;
    return willGet === 0 ? 0 : Math.round((done / willGet) * 100);
  }

  protected timing(item: QueueItem): string {
    if (item.status === 'paused') return item.pauseReason ?? 'Paused';
    if (!item.expectedStart) return 'Waiting';
    const start = new Date(item.expectedStart).getTime() <= Date.now() ? 'Sending now' : `Starts ${whenText(item.expectedStart)}`;
    return item.expectedFinish ? `${start} · done about ${whenText(item.expectedFinish)}` : start;
  }

  protected async move(item: QueueItem, direction: 'up' | 'down'): Promise<void> {
    this.busy.set(item.id);
    try {
      await this.api.move(item.id, direction);
      await this.loadQueue(false);
    } catch (e) {
      this.showError('Not moved', e);
    } finally {
      this.busy.set(null);
    }
  }

  protected async change(item: QueueItem, action: 'pause' | 'resume'): Promise<void> {
    this.busy.set(item.id);
    try {
      await this.broadcasts.change(item.id, action);
      await this.loadQueue(false);
    } catch (e) {
      this.showError('Not changed', e);
    } finally {
      this.busy.set(null);
    }
  }

  protected stop(item: QueueItem): void {
    this.confirm.confirm({
      header: 'Stop sending?',
      message: `"${item.name}": messages already sent stay sent. Everyone still waiting will not get it. This cannot be undone.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Stop sending',
      rejectLabel: 'Keep sending',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        this.busy.set(item.id);
        try {
          await this.broadcasts.change(item.id, 'cancel');
          await this.loadQueue(false);
        } catch (e) {
          this.showError('Not stopped', e);
        } finally {
          this.busy.set(null);
        }
      },
    });
  }

  // ---------- Settings ----------

  private useSettings(s: SendingSettings): void {
    this.settings.set(s);
    this.form = {
      dailyLimit: s.dailyLimit,
      chunkSize: s.chunkSize,
      chunkRestMinutes: s.chunkRestMinutes,
      sendFrom: s.sendFrom,
      sendUntil: s.sendUntil,
      minGapSeconds: s.minGapSeconds,
      maxGapSeconds: s.maxGapSeconds,
      anyTime: s.sendFrom === s.sendUntil,
    };
  }

  protected problem(): string | null {
    const f = this.form;
    if (!(f.dailyLimit >= 300 && f.dailyLimit <= 500)) return 'Messages a day: choose 300 to 500.';
    if (!(f.chunkSize >= 1 && f.chunkSize <= 500)) return 'Messages in one go: choose 1 to 500.';
    if (!(f.chunkRestMinutes >= 0 && f.chunkRestMinutes <= 600)) return 'Rest after each go: choose 0 to 600 minutes.';
    if (!(f.minGapSeconds >= 5 && f.minGapSeconds <= 600)) return 'Shortest pause: choose 5 to 600 seconds.';
    if (!(f.maxGapSeconds >= 5 && f.maxGapSeconds <= 600)) return 'Longest pause: choose 5 to 600 seconds.';
    if (Number(f.maxGapSeconds) < Number(f.minGapSeconds)) return 'The longest pause must not be shorter than the shortest one.';
    if (!f.anyTime && f.sendFrom === f.sendUntil) return 'Choose different start and end times, or tick "Any time".';
    return null;
  }

  protected async save(): Promise<void> {
    const problem = this.problem();
    if (problem) {
      this.toast.add({ severity: 'warn', summary: problem });
      return;
    }

    const f = this.form;
    this.saving.set(true);
    try {
      const saved = await this.api.saveSettings({
        dailyLimit: Number(f.dailyLimit),
        chunkSize: Number(f.chunkSize),
        chunkRestMinutes: Number(f.chunkRestMinutes),
        sendFrom: f.anyTime ? '00:00' : f.sendFrom,
        sendUntil: f.anyTime ? '00:00' : f.sendUntil,
        minGapSeconds: Number(f.minGapSeconds),
        maxGapSeconds: Number(f.maxGapSeconds),
      });
      this.useSettings(saved);
      this.toast.add({ severity: 'success', summary: 'Saved', detail: `About ${saved.fitsPerDay} messages fit in a day.` });
      void this.loadQueue(false);
    } catch (e) {
      this.showError('Not saved', e);
    } finally {
      this.saving.set(false);
    }
  }

  private showError(summary: string, e: unknown): void {
    this.toast.add({ severity: 'error', summary, detail: problemMessage(e), life: 6000 });
  }
}

/** Hours between two "HH:mm" times; past midnight counts (21:00 to 06:00 = 9 hours). */
export function windowHours(from: string, until: string): number {
  const toMin = (t: string) => {
    const [h, m] = t.split(':').map(Number);
    return (h || 0) * 60 + (m || 0);
  };
  const diff = (toMin(until) - toMin(from) + 1440) % 1440;
  return (diff === 0 ? 1440 : diff) / 60;
}
