import { DatePipe } from '@angular/common';
import { Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { highlightPlaceholders } from '../broadcasts/placeholders';
import { fileIcon, fileSize } from '../files/files.api';
import { chatSubtitle } from './chat-format';
import { ChatMessage } from './chats.api';

/** One sent message in a chat: the text as written, the file, and how far sending got. Tap to see who got it. */
@Component({
  selector: 'app-chat-bubble',
  imports: [DatePipe, RouterLink],
  template: `
    @let m = message();
    <a class="bubble" [routerLink]="['/chats', m.summary.id]" [attr.aria-label]="'Open details: ' + status()">
      @if (m.attachment; as f) {
        <span class="file"><i [class]="fileIcon(f.contentType)" aria-hidden="true"></i><span>{{ f.fileName }}</span><small>{{ fileSize(f.sizeBytes) }}</small></span>
      }
      <span class="text" [innerHTML]="html()"></span>
      <span class="meta">
        <span class="status" [class.live]="m.summary.status === 'sending'" [class.warn]="m.summary.status === 'paused'">{{ status() }}</span>
        <span class="time">{{ m.summary.createdAt | date: 'h:mm a' }}</span>
        <i [class]="m.summary.counts.read > 0 ? 'pi pi-check-circle read' : 'pi pi-check'" aria-hidden="true"></i>
      </span>
    </a>
  `,
  styles: `
    :host { display: flex; justify-content: flex-end; }
    .bubble {
      max-width: min(85%, 560px); display: flex; flex-direction: column; gap: 4px; padding: 7px 10px 5px; border-radius: 10px 2px 10px 10px;
      background: #d9fdd3; color: #111b21; text-decoration: none; box-shadow: 0 1px 0.5px rgb(11 20 26 / 13%);
    }
    .bubble:hover { filter: brightness(0.98); }
    .file { display: flex; align-items: center; gap: 8px; padding: 8px 10px; border-radius: 8px; background: rgb(255 255 255 / 65%); font-size: 0.88rem; }
    .file .pi { color: #0b7f65; font-size: 1.1rem; }
    .file span { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-weight: 500; }
    .file small { color: #667781; }
    .text { white-space: pre-wrap; word-break: break-word; }
    .meta { display: flex; align-items: center; justify-content: flex-end; gap: 6px; font-size: 0.72rem; color: #667781; }
    .status { margin-right: auto; padding-right: 10px; }
    .status.live { color: #0b7f65; font-weight: 600; }
    .status.warn { color: #b45309; font-weight: 600; }
    .read { color: #53bdeb; }
    :host ::ng-deep .ph { background: #fde68a; color: #78350f; border-radius: 4px; padding: 0 4px; font-size: 0.9em; }
  `,
})
export class ChatBubble {
  readonly message = input.required<ChatMessage>();

  protected readonly html = computed(() => highlightPlaceholders(this.message().text ?? ''));
  protected readonly status = computed(() => chatSubtitle(this.message().summary));
  protected readonly fileIcon = fileIcon;
  protected readonly fileSize = fileSize;
}
