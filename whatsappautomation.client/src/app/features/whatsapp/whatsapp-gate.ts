import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { ConfirmationService, ConfirmEventType, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../broadcasts/broadcasts.api';
import { BroadcastSummary } from '../broadcasts/broadcasts.models';
import { WhatsAppApi } from './whatsapp.api';

/**
 * Sending needs a connected WhatsApp number. Before anything is sent the user is stopped when it is not connected,
 * sent to the WhatsApp page to link it, and asked what to do with messages that are already waiting. When it
 * connects again, messages that were paused meanwhile are offered to resume or stop. Same rules as the phone app.
 */
@Injectable({ providedIn: 'root' })
export class WhatsAppGate {
  private readonly whatsApp = inject(WhatsAppApi);
  private readonly broadcasts = inject(BroadcastsApi);
  private readonly confirm = inject(ConfirmationService);
  private readonly toast = inject(MessageService);
  private readonly router = inject(Router);

  /** Resolves true when WhatsApp is connected and sending may go on. */
  async ensureConnected(): Promise<boolean> {
    try {
      if ((await this.whatsApp.account()).status === 'connected') {
        return true;
      }
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not check WhatsApp', detail: problemMessage(e), life: 6000 });
      return false;
    }

    const waiting = await this.waiting().catch(() => [] as BroadcastSummary[]);
    const people = waiting.reduce((sum, b) => sum + b.counts.pending, 0);
    const choice = await this.ask({
      header: 'WhatsApp is not connected',
      message:
        'Messages can be sent only while your WhatsApp number is connected. ' +
        (waiting.length > 0
          ? `${waiting.length} earlier ${waiting.length === 1 ? 'message is' : 'messages are'} waiting for ${people} ` +
            `${people === 1 ? 'person' : 'people'}: keep them, or stop them.`
          : ''),
      icon: 'pi pi-link',
      acceptLabel: 'Link WhatsApp',
      rejectLabel: waiting.length > 0 ? 'Stop waiting messages' : 'Not now',
    });
    if (choice === 'accept') {
      await this.router.navigate(['/whatsapp']);
    } else if (choice === 'reject' && waiting.length > 0) {
      await this.stopAll(waiting);
    }

    return false;
  }

  /** After WhatsApp connected again: asks whether to resume or stop messages that were paused meanwhile. */
  async offerPaused(): Promise<void> {
    const paused = (await this.waiting().catch(() => [] as BroadcastSummary[])).filter((b) => b.status === 'paused');
    if (paused.length === 0) return;

    const people = paused.reduce((sum, b) => sum + b.counts.pending, 0);
    const choice = await this.ask({
      header: `${paused.length} ${paused.length === 1 ? 'message is' : 'messages are'} paused`,
      message:
        `WhatsApp is connected again. ${people} ${people === 1 ? 'person is' : 'people are'} still waiting for ` +
        `${paused.length === 1 ? `"${paused[0].name}"` : 'these messages'}. Resume sending, or stop them? Close this to decide later.`,
      icon: 'pi pi-replay',
      acceptLabel: 'Resume sending',
      rejectLabel: 'Stop them',
    });
    if (choice === 'accept') {
      await this.changeAll(paused, 'resume', 'Sending resumed');
    } else if (choice === 'reject') {
      await this.stopAll(paused);
    }
  }

  private async waiting(): Promise<BroadcastSummary[]> {
    const all = await this.broadcasts.list();
    return all.filter((b) => (b.status === 'sending' || b.status === 'paused') && b.counts.pending > 0);
  }

  private async stopAll(list: BroadcastSummary[]): Promise<void> {
    const choice = await this.ask({
      header: `Stop ${list.length} ${list.length === 1 ? 'message' : 'messages'}?`,
      message: 'People who did not get them yet will not get them. Messages already sent stay sent.',
      icon: 'pi pi-stop-circle',
      acceptLabel: 'Stop',
      rejectLabel: 'Keep them',
      danger: true,
    });
    if (choice === 'accept') {
      await this.changeAll(list, 'cancel', 'Stopped');
    }
  }

  private async changeAll(list: BroadcastSummary[], action: 'resume' | 'cancel', done: string): Promise<void> {
    try {
      for (const b of list) {
        await this.broadcasts.change(b.id, action);
      }

      this.toast.add({ severity: 'success', summary: done, detail: `${list.length} ${list.length === 1 ? 'message' : 'messages'}` });
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not done', detail: problemMessage(e), life: 6000 });
    }
  }

  /** accept, reject (the second button), or close (the X or Escape). */
  private ask(o: {
    header: string;
    message: string;
    icon: string;
    acceptLabel: string;
    rejectLabel: string;
    danger?: boolean;
  }): Promise<'accept' | 'reject' | 'close'> {
    return new Promise((resolve) => {
      this.confirm.confirm({
        header: o.header,
        message: o.message,
        icon: o.icon,
        acceptLabel: o.acceptLabel,
        rejectLabel: o.rejectLabel,
        closable: true,
        closeOnEscape: true,
        acceptButtonProps: o.danger ? { severity: 'danger' } : undefined,
        rejectButtonProps: { severity: 'secondary', outlined: true },
        accept: () => resolve('accept'),
        // PrimeNG passes REJECT for the second button and CANCEL for the X or Escape (its typing leaves the argument out).
        reject: (...args: unknown[]) => resolve(args[0] === ConfirmEventType.REJECT ? 'reject' : 'close'),
      });
    });
  }
}
