import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, effect, inject, input, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { ChatBubble } from './chat-bubble';
import { ChatsApi, TargetChat } from './chats.api';
import { MessageBar } from './message-bar';
import { baseInput, ComposedMessage, SendFlow } from './send-flow';

/**
 * A group's or category's chat, like a WhatsApp group: the messages sent to it, and a message box to send the next
 * one. Each member still gets their own personal message. Tap the name for the group info.
 */
@Component({
  selector: 'app-target-chat-page',
  imports: [DatePipe, RouterLink, ChatBubble, MessageBar],
  templateUrl: './target-chat.page.html',
  styleUrl: './target-chat.page.css',
})
export class TargetChatPage {
  private readonly api = inject(ChatsApi);
  private readonly flow = inject(SendFlow);
  private readonly toast = inject(MessageService);
  private readonly bar = viewChild(MessageBar);

  /** From the route: data.kind and :id (withComponentInputBinding). */
  readonly kind = input.required<'group' | 'category'>();
  readonly id = input.required<string>();

  protected readonly chat = signal<TargetChat | null>(null);
  protected readonly sending = signal(false);

  /** Messages with a date line before the first message of each day. */
  protected readonly items = computed(() => {
    let lastDay = '';
    return (this.chat()?.messages ?? []).map((m) => {
      const day = m.summary.createdAt.slice(0, 10);
      const showDay = day !== lastDay;
      lastDay = day;
      return { m, showDay };
    });
  });

  constructor() {
    effect(() => {
      this.kind();
      this.id();
      this.chat.set(null);
      void this.load(true);
    });

    const timer = setInterval(() => {
      if (this.chat()?.messages.some((m) => m.summary.status === 'sending')) void this.load(false);
    }, 6000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected async send(message: ComposedMessage): Promise<void> {
    const chat = this.chat();
    if (!chat) return;
    this.sending.set(true);
    try {
      const request = baseInput(chat.name, message);
      if (chat.kind === 'group') request.groupIds = [chat.id];
      else request.categoryIds = [chat.id];
      if (await this.flow.confirmAndSend(request)) {
        this.bar()?.clear();
        await this.load(false);
      }
    } finally {
      this.sending.set(false);
    }
  }

  private async load(showErrors: boolean): Promise<void> {
    const kind = this.kind();
    const id = this.id();
    try {
      const chat = await this.api.target(kind, id);
      if (kind === this.kind() && id === this.id()) this.chat.set(chat);
      setTimeout(() => document.querySelector('.thread')?.scrollTo({ top: 1e9 }));
    } catch (e) {
      if (showErrors) this.toast.add({ severity: 'error', summary: 'Could not open the chat', detail: problemMessage(e), life: 6000 });
    }
  }
}
