import type { Logger } from 'pino';
import type { AuthStore } from './auth-store.ts';
import type { ConnectionStatus, EventSink } from './events.ts';
import type { MessageFile } from './media.ts';
import { AccountSession, errorSummary, GatewayError, type LinkMethod, type SessionBehaviour } from './session.ts';

export interface AccountStatus {
  accountId: string;
  status: ConnectionStatus;
  phoneNumber: string | null;
}

/** All linked WhatsApp numbers: one session per account (each app user links their own number). */
export class SessionManager {
  readonly #sessions = new Map<string, AccountSession>();
  readonly #store: AuthStore;
  readonly #emit: EventSink;
  readonly #log: Logger;
  readonly #baileysLog: Logger;
  readonly #behaviour: SessionBehaviour;

  constructor(store: AuthStore, emit: EventSink, log: Logger, baileysLog: Logger, behaviour: SessionBehaviour = {}) {
    this.#store = store;
    this.#emit = emit;
    this.#log = log;
    this.#baileysLog = baileysLog;
    this.#behaviour = behaviour;
  }

  /** Reconnects every account that was linked before the gateway stopped. */
  async resumeAll(): Promise<void> {
    const accountIds = await this.#store.listLinkedAccounts();
    this.#log.info({ count: accountIds.length }, 'Reconnecting linked WhatsApp numbers');
    for (const accountId of accountIds) {
      const session = this.#create(accountId);
      session.resume().catch((error: unknown) => {
        this.#log.error({ accountId, err: errorSummary(error) }, 'Could not reconnect');
      });
    }
  }

  status(accountId: string): AccountStatus {
    const session = this.#sessions.get(accountId);
    return {
      accountId,
      status: session?.status ?? 'not_linked',
      phoneNumber: session?.phoneNumber ?? null,
    };
  }

  async link(accountId: string, method: LinkMethod, phoneNumber: string | null): Promise<void> {
    const existing = this.#sessions.get(accountId);
    if (existing?.status === 'connected') {
      throw new GatewayError('already_linked', 409, 'This account already has a linked number. Unlink it first.');
    }

    existing?.shutdown();
    await this.#create(accountId).link(method, phoneNumber);
  }

  async unlink(accountId: string): Promise<void> {
    const session = this.#sessions.get(accountId);
    if (session) {
      await session.unlink();
      this.#sessions.delete(accountId);
    } else {
      await this.#store.clear(accountId);
    }
  }

  sendText(accountId: string, to: string, text: string, reference: string, file: MessageFile | null = null): Promise<string> {
    const session = this.#sessions.get(accountId);
    if (!session) {
      throw new GatewayError('not_connected', 409, 'This WhatsApp number is not connected.');
    }

    return session.sendText(to, text, reference, file);
  }

  checkNumbers(accountId: string, numbers: string[]): Promise<{ number: string; onWhatsApp: boolean }[]> {
    const session = this.#sessions.get(accountId);
    if (!session) {
      throw new GatewayError('not_connected', 409, 'This WhatsApp number is not connected.');
    }

    return session.checkNumbers(numbers);
  }

  shutdown(): void {
    for (const session of this.#sessions.values()) {
      session.shutdown();
    }

    this.#sessions.clear();
  }

  #create(accountId: string): AccountSession {
    const session = new AccountSession({
      accountId,
      store: this.#store,
      emit: this.#emit,
      log: this.#log,
      baileysLog: this.#baileysLog,
      ...this.#behaviour,
    });
    this.#sessions.set(accountId, session);
    return session;
  }
}
