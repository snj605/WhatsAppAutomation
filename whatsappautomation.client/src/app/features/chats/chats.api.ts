import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BroadcastAttachment, BroadcastSummary } from '../broadcasts/broadcasts.models';

export type ChatKind = 'group' | 'category' | 'birthdays' | 'broadcast';

/** One row of the Chats list (GET /api/v1/chats). */
export interface ChatListItem {
  kind: ChatKind;
  /** Group, category or broadcast id; null for today's Birthdays. */
  id: string | null;
  name: string;
  color: string | null;
  memberCount: number;
  lastText: string | null;
  lastAt: string | null;
  last: BroadcastSummary | null;
}

/** One message in a chat: a broadcast with its text, file and progress. */
export interface ChatMessage {
  summary: BroadcastSummary;
  text: string | null;
  attachment: BroadcastAttachment | null;
}

export interface TargetChat {
  kind: 'group' | 'category';
  id: string;
  name: string;
  color: string | null;
  memberCount: number;
  messages: ChatMessage[];
}

export type BirthdayStatus = 'not_sent' | 'pending' | 'sent' | 'delivered' | 'read' | 'failed' | 'skipped';

export interface BirthdayPerson {
  contactId: string;
  name: string;
  phoneNumber: string;
  turnsAge: number | null;
  optedOut: boolean;
  status: BirthdayStatus;
}

export interface BirthdaysChat {
  date: string;
  people: BirthdayPerson[];
  messages: ChatMessage[];
}

@Injectable({ providedIn: 'root' })
export class ChatsApi {
  private readonly http = inject(HttpClient);

  list = () => firstValueFrom(this.http.get<ChatListItem[]>('/api/v1/chats'));
  target = (kind: 'group' | 'category', id: string) => firstValueFrom(this.http.get<TargetChat>(`/api/v1/chats/${kind}/${id}`));
  birthdays = () => firstValueFrom(this.http.get<BirthdaysChat>('/api/v1/chats/birthdays'));
}

/** Where a row of the Chats list opens. */
export function chatLink(c: ChatListItem): string[] {
  switch (c.kind) {
    case 'group':
      return ['/chats', 'group', c.id!];
    case 'category':
      return ['/chats', 'category', c.id!];
    case 'birthdays':
      return ['/chats', 'birthdays'];
    default:
      return ['/chats', c.id!];
  }
}
