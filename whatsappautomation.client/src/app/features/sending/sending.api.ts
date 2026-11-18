import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BroadcastCounts, BroadcastSummary } from '../broadcasts/broadcasts.models';

/** Shapes of /api/v1/sending/settings and /api/v1/broadcasts/queue. They mirror the server DTOs. */
export interface SendingSettings {
  dailyLimit: number;
  sendFrom: string;
  sendUntil: string;
  timeZone: string;
  fitsPerDay: number;
  /** About how long one message takes on average, pauses and rests included (the system's own rhythm). */
  averageSeconds: number;
}

export interface QueueItem {
  id: string;
  name: string;
  kind: 'occasion' | 'broadcast';
  status: 'sending' | 'paused';
  pauseReason: string | null;
  counts: BroadcastCounts;
  expectedStart: string | null;
  expectedFinish: string | null;
  canMoveUp: boolean;
  canMoveDown: boolean;
}

export interface Queue {
  connected: boolean;
  dailyLimit: number;
  sentToday: number;
  leftToday: number;
  sendingHours: string;
  inSendingHours: boolean;
  nextSendAt: string | null;
  waiting: string | null;
  items: QueueItem[];
}

export type ResultList = 'all' | 'pending' | 'sent' | 'delivered' | 'read' | 'failed' | 'skipped';

export interface RecipientRow {
  id: string;
  contactId: string | null;
  name: string;
  phoneNumber: string;
  status: string;
  reason: string | null;
  attempts: number;
  sentAt: string | null;
  deliveredAt: string | null;
  readAt: string | null;
}

export interface RecipientsPage {
  items: RecipientRow[];
  total: number;
  /** The message's name, state and counts. */
  summary: BroadcastSummary;
}

@Injectable({ providedIn: 'root' })
export class SendingApi {
  private readonly http = inject(HttpClient);

  settings = () => firstValueFrom(this.http.get<SendingSettings>('/api/v1/sending/settings'));
  saveSettings = (s: Omit<SendingSettings, 'timeZone' | 'fitsPerDay' | 'averageSeconds'>) =>
    firstValueFrom(this.http.put<SendingSettings>('/api/v1/sending/settings', s));
  queue = () => firstValueFrom(this.http.get<Queue>('/api/v1/broadcasts/queue'));
  move = (id: string, direction: 'up' | 'down') => firstValueFrom(this.http.post<void>(`/api/v1/broadcasts/${id}/move`, { direction }));
  retry = (id: string, recipientIds: string[] | null) =>
    firstValueFrom(this.http.post<BroadcastSummary>(`/api/v1/broadcasts/${id}/retry`, { recipientIds }));
  recipients = (id: string, status: ResultList, page = 1, pageSize = 50) =>
    firstValueFrom(this.http.get<RecipientsPage>(`/api/v1/broadcasts/${id}/recipients`, { params: { status, page, pageSize } }));
}
