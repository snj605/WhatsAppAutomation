import { inject, Injectable } from '@angular/core';
import { ConfirmationService, MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BroadcastsApi } from '../broadcasts/broadcasts.api';
import { BroadcastInput, BroadcastPreview, BroadcastSummary } from '../broadcasts/broadcasts.models';
import { WhatsAppGate } from '../whatsapp/whatsapp-gate';

/**
 * Sending from a chat: check who would get it (and who is skipped and why), ask once, then start sending.
 * Resolves with the new broadcast, or null when nothing was sent.
 */
@Injectable({ providedIn: 'root' })
export class SendFlow {
  private readonly api = inject(BroadcastsApi);
  private readonly confirm = inject(ConfirmationService);
  private readonly toast = inject(MessageService);
  private readonly gate = inject(WhatsAppGate);

  /** Nothing is sent while WhatsApp is not connected: the user is offered to link it. */
  async confirmAndSend(input: BroadcastInput): Promise<BroadcastSummary | null> {
    if (!(await this.gate.ensureConnected())) return null;

    let preview: BroadcastPreview;
    try {
      preview = await this.api.preview(input);
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Could not check the message', detail: problemMessage(e), life: 6000 });
      return null;
    }

    const skipped = preview.total - preview.willSend;
    if (preview.willSend === 0) {
      this.toast.add({
        severity: 'warn',
        summary: 'Nobody can get this message',
        detail: preview.skipped[0]?.reason ?? 'There is nobody in this chat yet.',
        life: 7000,
      });
      return null;
    }

    const example = preview.samples[0];
    const accepted = await new Promise<boolean>((resolve) => {
      this.confirm.confirm({
        header: `Send to ${preview.willSend} ${preview.willSend === 1 ? 'person' : 'people'}?`,
        message:
          (example ? `${example.contactName} will get: "${example.text}". ` : '') +
          (skipped > 0 ? `${skipped} will be skipped (${preview.skipped[0]?.reason ?? 'missing details'}). ` : '') +
          (preview.estimatedDays > 1 ? `Sending takes about ${preview.estimatedDays} days. ` : 'They go out one by one. ') +
          'You can pause or stop any time.',
        icon: 'pi pi-send',
        acceptLabel: 'Send',
        rejectLabel: 'Not yet',
        rejectButtonProps: { severity: 'secondary', outlined: true },
        accept: () => resolve(true),
        reject: () => resolve(false),
      });
    });
    if (!accepted) return null;

    try {
      const created = await this.api.create(input);
      this.toast.add({ severity: 'success', summary: 'Sending started', detail: `${created.counts.pending} will get it one by one.` });
      return created;
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not sent', detail: problemMessage(e), life: 6000 });
      return null;
    }
  }
}

/** A message ready to send from a chat: the text (with placeholders) and an optional uploaded file. */
export interface ComposedMessage {
  text: string;
  attachmentId: string | null;
  /** The file is a video over 100 MB and the user agreed it goes as a file. */
  sendVideoAsDocument?: boolean;
}

/** The parts every chat send needs; the caller adds who gets it. */
export function baseInput(name: string, message: ComposedMessage): BroadcastInput {
  return {
    name,
    text: message.text,
    textEn: null,
    textHi: null,
    textGu: null,
    categoryIds: [],
    groupIds: [],
    contactIds: [],
    attachmentId: message.attachmentId,
    sendVideoAsDocument: message.sendVideoAsDocument === true,
  };
}
