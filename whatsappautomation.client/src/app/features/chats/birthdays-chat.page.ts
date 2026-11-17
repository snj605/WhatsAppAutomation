import { DatePipe } from '@angular/common';
import { Component, computed, DestroyRef, inject, OnInit, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MessageService } from 'primeng/api';
import { problemMessage } from '../../core/http/problem';
import { BIRTHDAY_TEXT } from '../birthdays/birthday-wishes';
import { avatarColors, initialsOf } from './chat-format';
import { ChatBubble } from './chat-bubble';
import { BirthdayPerson, BirthdaysChat, BirthdayStatus, ChatsApi } from './chats.api';
import { MessageBar } from './message-bar';
import { baseInput, ComposedMessage, SendFlow } from './send-flow';

const STATUS: Record<BirthdayStatus, { label: string; icon: string; tone: string }> = {
  not_sent: { label: 'Not sent', icon: 'pi pi-circle', tone: 'todo' },
  pending: { label: 'Waiting', icon: 'pi pi-clock', tone: 'wait' },
  sent: { label: 'Sent', icon: 'pi pi-check', tone: 'done' },
  delivered: { label: 'Delivered', icon: 'pi pi-check-circle', tone: 'done' },
  read: { label: 'Read', icon: 'pi pi-eye', tone: 'read' },
  failed: { label: 'Failed', icon: 'pi pi-times-circle', tone: 'bad' },
  skipped: { label: 'Skipped', icon: 'pi pi-minus-circle', tone: 'bad' },
};

/**
 * Today's Birthdays: one chat for the whole day. A checklist shows everyone with a birthday today and whether they
 * got wishes yet; the ones not wished yet are ticked, and the message box sends them a personal wish.
 */
@Component({
  selector: 'app-birthdays-chat-page',
  imports: [DatePipe, RouterLink, ChatBubble, MessageBar],
  templateUrl: './birthdays-chat.page.html',
  styleUrl: './birthdays-chat.page.css',
})
export class BirthdaysChatPage implements OnInit {
  private readonly api = inject(ChatsApi);
  private readonly flow = inject(SendFlow);
  private readonly toast = inject(MessageService);
  private readonly bar = viewChild(MessageBar);

  protected readonly wishText = BIRTHDAY_TEXT;
  protected readonly initials = initialsOf;
  protected readonly avatar = avatarColors;
  protected readonly chat = signal<BirthdaysChat | null>(null);
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly sending = signal(false);
  private touched = false;

  protected readonly done = computed(() => (this.chat()?.people ?? []).filter((p) => p.status !== 'not_sent').length);
  protected readonly total = computed(() => this.chat()?.people.length ?? 0);

  constructor() {
    const timer = setInterval(() => {
      if (this.chat()?.people.some((p) => p.status === 'pending')) void this.load(false);
    }, 6000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  async ngOnInit(): Promise<void> {
    await this.load(true);
  }

  protected status(p: BirthdayPerson) {
    return STATUS[p.status];
  }

  protected canPick(p: BirthdayPerson): boolean {
    return !p.optedOut;
  }

  protected isPicked(id: string): boolean {
    return this.selected().has(id);
  }

  protected toggle(p: BirthdayPerson): void {
    if (!this.canPick(p)) return;
    this.touched = true;
    const next = new Set(this.selected());
    if (next.has(p.contactId)) next.delete(p.contactId);
    else next.add(p.contactId);
    this.selected.set(next);
  }

  protected pickNotSent(): void {
    this.touched = true;
    this.selected.set(new Set(this.notSent().map((p) => p.contactId)));
  }

  protected async send(message: ComposedMessage): Promise<void> {
    const ids = [...this.selected()];
    if (ids.length === 0) return;
    this.sending.set(true);
    try {
      const day = new Date().toLocaleDateString('en-IN', { day: 'numeric', month: 'short' });
      const request = { ...baseInput(`Birthday wishes ${day}`, message), contactIds: ids, forBirthday: true };
      if (await this.flow.confirmAndSend(request)) {
        this.bar()?.clear();
        this.touched = false;
        await this.load(false);
      }
    } catch (e) {
      this.toast.add({ severity: 'error', summary: 'Not sent', detail: problemMessage(e), life: 6000 });
    } finally {
      this.sending.set(false);
    }
  }

  private notSent(): BirthdayPerson[] {
    return (this.chat()?.people ?? []).filter((p) => p.status === 'not_sent' && this.canPick(p));
  }

  private async load(showErrors: boolean): Promise<void> {
    try {
      this.chat.set(await this.api.birthdays());
      // Until the user changes it, the ones not wished yet are ticked.
      if (!this.touched) this.selected.set(new Set(this.notSent().map((p) => p.contactId)));
    } catch (e) {
      if (showErrors) this.toast.add({ severity: 'error', summary: 'Could not load birthdays', detail: problemMessage(e), life: 6000 });
    }
  }
}
