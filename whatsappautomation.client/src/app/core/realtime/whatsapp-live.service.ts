import { inject, Injectable, signal } from '@angular/core';
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { WhatsAppCheckSummary } from '../../features/contacts/contacts.models';
import { AppNotification } from '../../features/notifications/notifications.api';
import { OutgoingMessage, WhatsAppAccount } from '../../features/whatsapp/whatsapp.models';
import { AuthService } from '../auth/auth.service';

/**
 * Live WhatsApp updates from the server (SignalR hub /hubs/whatsapp): QR codes while linking,
 * the phone code, connection changes and message ticks. Only this user's updates arrive.
 * Pages call start() when they open and stop() when they close.
 */
@Injectable({ providedIn: 'root' })
export class WhatsAppLiveService {
  private readonly auth = inject(AuthService);
  private connection: HubConnection | null = null;
  private users = 0;

  /** Whether live updates are flowing. When false, the page shows a note and can reload by hand. */
  readonly connected = signal(false);
  readonly qr$ = new Subject<string>();
  readonly pairingCode$ = new Subject<string>();
  readonly account$ = new Subject<WhatsAppAccount>();
  readonly message$ = new Subject<OutgoingMessage>();
  /** After numbers were checked on WhatsApp: the new counts. */
  readonly contacts$ = new Subject<WhatsAppCheckSummary>();
  /** A new notification for the bell (number logged out, sending paused, occasion wishes sent, ...). */
  readonly notification$ = new Subject<AppNotification>();

  async start(): Promise<void> {
    this.users++;
    if (this.connection) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/whatsapp', { accessTokenFactory: () => this.auth.freshToken() })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on('qr', (e: { qr: string }) => this.qr$.next(e.qr));
    connection.on('pairingCode', (e: { code: string }) => this.pairingCode$.next(e.code));
    connection.on('account', (a: WhatsAppAccount) => this.account$.next(a));
    connection.on('message', (m: OutgoingMessage) => this.message$.next(m));
    connection.on('contacts', (s: WhatsAppCheckSummary) => this.contacts$.next(s));
    connection.on('notification', (n: AppNotification) => this.notification$.next(n));
    connection.onreconnecting(() => this.connected.set(false));
    connection.onreconnected(() => this.connected.set(true));
    connection.onclose(() => this.connected.set(false));
    this.connection = connection;

    try {
      await connection.start();
      this.connected.set(true);
    } catch {
      this.connected.set(false);
    }
  }

  async stop(): Promise<void> {
    this.users = Math.max(0, this.users - 1);
    if (this.users > 0 || !this.connection) {
      return;
    }

    const connection = this.connection;
    this.connection = null;
    this.connected.set(false);
    if (connection.state !== HubConnectionState.Disconnected) {
      await connection.stop();
    }
  }
}
