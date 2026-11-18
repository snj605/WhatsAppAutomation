import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastSummary } from '../broadcasts/broadcasts.models';
import { initials, statusLook, whenText } from '../modules/time-text';
import { RecipientRow, ResultList, SendingApi } from './sending.api';

const LISTS: { id: ResultList; label: string }[] = [
  { id: 'all', label: 'All' },
  { id: 'pending', label: 'Waiting' },
  { id: 'sent', label: 'Sent' },
  { id: 'delivered', label: 'Delivered' },
  { id: 'read', label: 'Read' },
  { id: 'failed', label: 'Failed' },
  { id: 'skipped', label: 'Skipped' },
];
const PAGE = 50;

/**
 * Results of one message: who got it, by list (sent, delivered, read, failed, skipped), and "Send again" for the
 * failed ones (all of them, or the ones ticked). Numbers not on WhatsApp are never sent again.
 */
@Component({
  selector: 'app-results-page',
  imports: [RouterLink],
  templateUrl: './results.page.html',
  styleUrls: ['../../shared/approved.css', './results.page.css'],
})
export class ResultsPage {
  private readonly api = inject(SendingApi);
  private readonly toast = inject(MessageService);
  private readonly confirm = inject(ConfirmationService);

  readonly id = input.required<string>();

  protected readonly lists = LISTS;
  protected readonly whenText = whenText;
  protected readonly initials = initials;
  protected readonly statusLook = statusLook;

  protected readonly details = signal<BroadcastSummary | null>(null);
  protected readonly list = signal<ResultList>('all');
  protected readonly rows = signal<RecipientRow[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);
  protected readonly picked = signal<ReadonlySet<string>>(new Set());
  protected readonly retrying = signal(false);

  protected readonly pages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE)));
  protected readonly failedCount = computed(() => this.details()?.counts.failed ?? 0);

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => {
        this.details.set(null);
        void this.load(1, 'all', id);
      });
    });
  }

  protected count(l: ResultList): number | null {
    const c = this.details()?.counts;
    if (!c) return null;
    switch (l) {
      case 'all':
        return c.total;
      case 'pending':
        return c.pending;
      case 'sent':
        return c.sent;
      case 'delivered':
        return c.delivered;
      case 'read':
        return c.read;
      case 'failed':
        return c.failed;
      case 'skipped':
        return c.skipped;
    }
  }

  protected async load(page: number, list = this.list(), id = this.id()): Promise<void> {
    this.list.set(list);
    this.loading.set(true);
    try {
      const result = await this.api.recipients(id, list, page, PAGE);
      this.details.set(result.summary);
      this.rows.set(result.items);
      this.total.set(result.total);
      this.page.set(page);
      this.picked.set(new Set());
      this.error.set(null);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.loading.set(false);
    }
  }

  protected canPick(r: RecipientRow): boolean {
    return r.status === 'failed';
  }

  protected toggle(r: RecipientRow): void {
    if (!this.canPick(r)) return;
    const next = new Set(this.picked());
    if (!next.delete(r.id)) next.add(r.id);
    this.picked.set(next);
  }

  protected rowTime(r: RecipientRow): string | null {
    const at = r.readAt ?? r.deliveredAt ?? r.sentAt;
    return at ? whenText(at) : null;
  }

  protected retry(only: boolean): void {
    const ids = only ? [...this.picked()] : null;
    const n = only ? ids!.length : this.failedCount();
    if (n === 0) return;
    this.confirm.confirm({
      header: `Send again to ${n} ${n === 1 ? 'person' : 'people'}?`,
      message: 'They go back in the queue and go out one by one. Numbers not on WhatsApp are left out.',
      icon: 'pi pi-replay',
      acceptLabel: 'Send again',
      rejectLabel: 'Not now',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: async () => {
        this.retrying.set(true);
        try {
          await this.api.retry(this.id(), ids);
          this.toast.add({ severity: 'success', summary: 'Sending again', detail: 'They are back in the queue.' });
          await this.load(1);
        } catch (e) {
          this.toast.add({ severity: 'error', summary: 'Not sent again', detail: problemMessage(e), life: 6000 });
        } finally {
          this.retrying.set(false);
        }
      },
    });
  }

  /** A printable report (the browser's Print → Save as PDF). */
  protected print(): void {
    window.print();
  }
}
