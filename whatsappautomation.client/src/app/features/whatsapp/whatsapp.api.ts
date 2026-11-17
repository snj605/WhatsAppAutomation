import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { LinkMethod, OutgoingMessage, WhatsAppAccount } from './whatsapp.models';

/** Calls to /api/v1/whatsapp: the signed-in user's own number. */
@Injectable({ providedIn: 'root' })
export class WhatsAppApi {
  private readonly http = inject(HttpClient);

  account = () => firstValueFrom(this.http.get<WhatsAppAccount>('/api/v1/whatsapp'));
  link = (method: LinkMethod, phoneNumber: string | null) =>
    firstValueFrom(this.http.post<WhatsAppAccount>('/api/v1/whatsapp/link', { method, phoneNumber }));
  unlink = () => firstValueFrom(this.http.post<WhatsAppAccount>('/api/v1/whatsapp/unlink', null));
  messages = () => firstValueFrom(this.http.get<OutgoingMessage[]>('/api/v1/whatsapp/messages'));
  send = (to: string, text: string) =>
    firstValueFrom(this.http.post<OutgoingMessage>('/api/v1/whatsapp/messages', { to, text }));
}
