import type { Boom } from '@hapi/boom';
import type { Logger } from 'pino';
import {
  Browsers,
  DisconnectReason,
  fetchLatestWaWebVersion,
  generateWAMessageFromContent,
  makeCacheableSignalKeyStore,
  makeWASocket,
  prepareWAMessageMedia,
  WAMessageStatus,
  type AnyMediaMessageContent,
  type proto,
  type WASocket,
} from 'baileys';
import type { AuthStore } from './auth-store.ts';
import { typingDelayMs } from './human.ts';
import { MEDIA_REUSE_MS, mediaKind, type MessageFile, withCaption } from './media.ts';
import type { ConnectionStatus, EventSink, MessageStatus } from './events.ts';
import { phoneFromJid, toDigits } from './phone.ts';

/** An expected failure the HTTP layer turns into a status code and an error code. */
export class GatewayError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, status: number, message: string) {
    super(message);
    this.code = code;
    this.status = status;
  }
}

export type LinkMethod = 'qr' | 'code';

/** Sending behaviour that comes from configuration. */
export interface SessionBehaviour {
  /** Show "typing…" before each message (default true). */
  simulateTyping?: boolean;
  /** Fetches a file's bytes from the API (once per broadcast). Without it, files cannot be sent. */
  loadFile?: (fileId: string) => Promise<Buffer>;
}

const MEDIA_CACHE_SIZE = 20;

export interface SessionOptions extends SessionBehaviour {
  accountId: string;
  store: AuthStore;
  emit: EventSink;
  log: Logger;
  baileysLog: Logger;
  /** How long a QR or phone code may wait to be used. */
  linkTimeoutMs?: number;
}

const LINK_TIMEOUT_MS = 3 * 60_000;
const MAX_BACKOFF_MS = 5 * 60_000;
const SENT_CACHE_SIZE = 500;

/**
 * One WhatsApp number: its socket, linking, reconnecting and sending.
 *
 * Reconnecting depends on why the connection closed (Baileys' DisconnectReason):
 * - restart required (normal right after linking): reconnect at once;
 * - logged out / bad session / device mismatch: delete the saved login, ask the user to link again;
 * - forbidden (number restricted or banned): stop, never reconnect automatically;
 * - replaced (the same login opened somewhere else): stop;
 * - anything else (network, timeout): reconnect after 2 s, 4 s, 8 s … up to 5 minutes.
 */
export class AccountSession {
  readonly accountId: string;
  readonly #store: AuthStore;
  readonly #emit: EventSink;
  readonly #log: Logger;
  readonly #baileysLog: Logger;
  readonly #linkTimeoutMs: number;
  readonly #simulateTyping: boolean;
  readonly #loadFile: ((fileId: string) => Promise<Buffer>) | null;
  /** Files already uploaded to WhatsApp, by the API's file id: the same upload is reused for every recipient. */
  readonly #media = new Map<string, { message: proto.IMessage; at: number }>();

  #socket: WASocket | null = null;
  #status: ConnectionStatus = 'not_linked';
  #phoneNumber: string | null = null;
  #linking: { method: LinkMethod; phoneDigits: string | null; codeRequested: boolean; reachedWhatsApp: boolean } | null =
    null;
  #linkTimer: NodeJS.Timeout | null = null;
  #reconnectTimer: NodeJS.Timeout | null = null;
  #reconnectAttempts = 0;
  #stopped = false;
  #sendQueue: Promise<unknown> = Promise.resolve();
  /** Recently sent messages: WhatsApp may ask for one again ("retry"), and statuses map back to the API's id. */
  readonly #sent = new Map<string, { reference: string; message: proto.IMessage }>();
  readonly #sentByReference = new Map<string, string>();

  constructor(options: SessionOptions) {
    this.accountId = options.accountId;
    this.#store = options.store;
    this.#emit = options.emit;
    this.#log = options.log.child({ accountId: options.accountId });
    this.#baileysLog = options.baileysLog;
    this.#linkTimeoutMs = options.linkTimeoutMs ?? LINK_TIMEOUT_MS;
    this.#simulateTyping = options.simulateTyping ?? true;
    this.#loadFile = options.loadFile ?? null;
  }

  get status(): ConnectionStatus {
    return this.#status;
  }

  get phoneNumber(): string | null {
    return this.#phoneNumber;
  }

  get isActive(): boolean {
    return !this.#stopped;
  }

  /** Reconnects with the saved login (gateway start-up). */
  async resume(): Promise<void> {
    this.#setStatus('connecting');
    await this.#connect();
  }

  /** Starts linking from scratch: any saved login of this account is deleted first. */
  async link(method: LinkMethod, phoneNumber: string | null): Promise<void> {
    const phoneDigits = method === 'code' ? toDigits(phoneNumber ?? '') : null;
    if (method === 'code' && !phoneDigits) {
      throw new GatewayError('invalid_phone', 400, 'Linking with a code needs the phone number in international form.');
    }

    await this.#store.clear(this.accountId);
    this.#linking = { method, phoneDigits, codeRequested: false, reachedWhatsApp: false };
    this.#linkTimer = setTimeout(() => void this.#linkTimedOut(), this.#linkTimeoutMs);
    this.#setStatus('linking');
    await this.#connect();
  }

  /** Logs out on WhatsApp (the device disappears from the phone's Linked devices) and deletes the saved login. */
  async unlink(): Promise<void> {
    const socket = this.#socket;
    this.#stop();
    if (socket && this.#status === 'connected') {
      try {
        await socket.logout();
      } catch (error) {
        this.#log.warn({ err: errorSummary(error) }, 'Logout call failed; deleting the saved login anyway');
      }
    } else {
      socket?.end(undefined);
    }

    await this.#store.clear(this.accountId);
    this.#phoneNumber = null;
    this.#setStatus('not_linked', 'unlinked');
  }

  /** Closes the socket without logging out (gateway shutdown). The saved login stays. */
  shutdown(): void {
    const socket = this.#socket;
    this.#stop();
    socket?.end(undefined);
  }

  /**
   * Sends one text message. Messages of one account go out strictly one at a time.
   * "reference" is the API's id for the message: sending the same reference twice returns the
   * first WhatsApp message id instead of sending again.
   */
  sendText(to: string, text: string, reference: string, file: MessageFile | null = null): Promise<string> {
    const run = this.#sendQueue.then(() => this.#sendTextNow(to, text, reference, file));
    this.#sendQueue = run.catch(() => undefined);
    return run;
  }

  /**
   * Asks WhatsApp which of these numbers (E.164) have WhatsApp, in one query. Numbers that are not in
   * international form count as not on WhatsApp. Throws when WhatsApp gives no answer, so nothing is
   * marked wrongly.
   */
  async checkNumbers(numbers: string[]): Promise<{ number: string; onWhatsApp: boolean }[]> {
    const socket = this.#socket;
    if (!socket || this.#status !== 'connected') {
      throw new GatewayError('not_connected', 409, 'This WhatsApp number is not connected.');
    }

    const valid = numbers.filter((n) => toDigits(n) !== null);
    const found = new Set<string>();
    if (valid.length > 0) {
      const results = await socket.onWhatsApp(...valid.map((n) => toDigits(n)!));
      if (!results) {
        throw new GatewayError('check_failed', 502, 'WhatsApp did not answer the number check. Try again later.');
      }

      for (const result of results) {
        const phone = result.exists ? phoneFromJid(result.jid) : null;
        if (phone) {
          found.add(phone);
        }
      }
    }

    return numbers.map((number) => ({ number, onWhatsApp: found.has(number) }));
  }

  async #sendTextNow(to: string, text: string, reference: string, file: MessageFile | null): Promise<string> {
    const already = this.#sentByReference.get(reference);
    if (already) {
      return already;
    }

    const socket = this.#socket;
    if (!socket || this.#status !== 'connected') {
      throw new GatewayError('not_connected', 409, 'This WhatsApp number is not connected.');
    }

    const digits = toDigits(to);
    if (!digits) {
      throw new GatewayError('invalid_phone', 400, 'The phone number must be in international form.');
    }

    const [lookup] = (await socket.onWhatsApp(digits)) ?? [];
    if (!lookup?.exists) {
      throw new GatewayError('not_on_whatsapp', 422, 'This number is not on WhatsApp.');
    }

    if (this.#simulateTyping) {
      await this.#showTyping(socket, lookup.jid, text);
    }

    if (file) {
      return this.#sendWithFile(socket, lookup.jid, text, reference, file);
    }

    const content = { text };
    const sent = await socket.sendMessage(lookup.jid, content);
    const id = sent?.key.id;
    if (!id) {
      throw new GatewayError('send_failed', 502, 'WhatsApp did not accept the message.');
    }

    this.#remember(id, reference, sent.message ?? { conversation: text });
    return id;
  }

  /**
   * Sends the file with this person's text as caption. The file is uploaded to WhatsApp only the first time (and
   * again after MEDIA_REUSE_MS or a gateway restart); later recipients get the same upload, like a forward.
   */
  async #sendWithFile(socket: WASocket, jid: string, text: string, reference: string, file: MessageFile): Promise<string> {
    const prepared = await this.#uploaded(socket, file);
    const message = generateWAMessageFromContent(jid, withCaption(prepared, text), { userJid: socket.user?.id ?? jid });
    const id = message.key.id;
    if (!id || !message.message) {
      throw new GatewayError('send_failed', 502, 'WhatsApp did not accept the message.');
    }

    await socket.relayMessage(jid, message.message, { messageId: id });
    this.#remember(id, reference, message.message);
    return id;
  }

  async #uploaded(socket: WASocket, file: MessageFile): Promise<proto.IMessage> {
    const cached = this.#media.get(file.id);
    if (cached && Date.now() - cached.at < MEDIA_REUSE_MS) {
      return cached.message;
    }

    if (!this.#loadFile) {
      throw new GatewayError('files_unavailable', 503, 'This gateway cannot fetch files.');
    }

    const data = await this.#loadFile(file.id);
    const kind = file.asDocument ? 'document' : mediaKind(file.mimeType);
    const content: AnyMediaMessageContent =
      kind === 'image'
        ? { image: data, mimetype: file.mimeType }
        : kind === 'video'
          ? { video: data, mimetype: file.mimeType }
          : { document: data, mimetype: file.mimeType, fileName: file.name };
    const message = await prepareWAMessageMedia(content, { upload: socket.waUploadToServer, logger: this.#baileysLog });
    this.#log.info({ fileId: file.id, kind, bytes: data.length }, 'File uploaded to WhatsApp; it is reused for the next recipients');

    this.#media.set(file.id, { message, at: Date.now() });
    if (this.#media.size > MEDIA_CACHE_SIZE) {
      const oldest = this.#media.keys().next().value;
      if (oldest !== undefined) this.#media.delete(oldest);
    }

    return message;
  }

  /**
   * Shows "typing…" in the recipient's chat for a few seconds, like a person writing. Best effort: if it
   * fails, the message is still sent. (WhatsApp may not show it while this device is not "online".)
   */
  async #showTyping(socket: WASocket, jid: string, text: string): Promise<void> {
    try {
      await socket.sendPresenceUpdate('composing', jid);
      await new Promise((resolve) => setTimeout(resolve, typingDelayMs(text)));
      await socket.sendPresenceUpdate('paused', jid);
    } catch (error) {
      this.#log.debug({ err: errorSummary(error) }, 'Typing indicator failed; sending anyway');
    }
  }

  #remember(id: string, reference: string, message: proto.IMessage): void {
    this.#sent.set(id, { reference, message });
    this.#sentByReference.set(reference, id);
    if (this.#sent.size > SENT_CACHE_SIZE) {
      const [oldestId, oldest] = this.#sent.entries().next().value as [string, { reference: string }];
      this.#sent.delete(oldestId);
      this.#sentByReference.delete(oldest.reference);
    }
  }

  async #connect(): Promise<void> {
    this.#stopped = false;
    const { state, saveCreds } = await this.#store.load(this.accountId);
    const { version } = await fetchLatestWaWebVersion().catch(() => ({ version: undefined }));

    const socket = makeWASocket({
      ...(version ? { version } : {}),
      auth: { creds: state.creds, keys: makeCacheableSignalKeyStore(state.keys, this.#baileysLog) },
      logger: this.#baileysLog,
      browser: Browsers.windows('Chrome'),
      // Do not appear "online": the phone keeps getting its notifications.
      markOnlineOnConnect: false,
      // A send-only app does not need old chats.
      syncFullHistory: false,
      shouldSyncHistoryMessage: () => false,
      getMessage: async (key) => (key.id ? this.#sent.get(key.id)?.message : undefined),
    });
    this.#socket = socket;

    socket.ev.on('creds.update', () => {
      saveCreds().catch((error: unknown) => this.#log.error({ err: errorSummary(error) }, 'Saving the login failed'));
    });

    socket.ev.on('connection.update', (update) => {
      if (socket !== this.#socket) {
        return; // an old socket we already replaced
      }

      if (update.qr) {
        void this.#onQr(socket, update.qr);
      }

      if (update.connection === 'open') {
        void this.#onOpen(socket);
      } else if (update.connection === 'close') {
        const code = (update.lastDisconnect?.error as Boom | undefined)?.output?.statusCode;
        void this.#onClose(code);
      }
    });

    socket.ev.on('messages.update', (updates) => {
      for (const { key, update } of updates) {
        if (!key.fromMe || !key.id || update.status == null) {
          continue;
        }

        const sent = this.#sent.get(key.id);
        const status = mapMessageStatus(update.status);
        if (sent && status) {
          this.#emit({
            type: 'message_status',
            accountId: this.accountId,
            reference: sent.reference,
            status,
            at: new Date().toISOString(),
          });
        }
      }
    });
  }

  async #onQr(socket: WASocket, qr: string): Promise<void> {
    const linking = this.#linking;
    if (linking) {
      linking.reachedWhatsApp = true;
    }

    if (!linking) {
      // A QR while resuming means the saved login is no longer valid.
      this.#log.warn('Saved login was rejected; the number must be linked again');
      this.#stop();
      socket.end(undefined);
      await this.#store.clear(this.accountId);
      this.#setStatus('logged_out', 'session_invalid');
      return;
    }

    if (linking.method === 'qr') {
      this.#emit({ type: 'qr', accountId: this.accountId, qr });
      return;
    }

    if (!linking.codeRequested && linking.phoneDigits) {
      linking.codeRequested = true;
      try {
        const code = await socket.requestPairingCode(linking.phoneDigits);
        this.#emit({ type: 'pairing_code', accountId: this.accountId, code });
      } catch (error) {
        this.#log.warn({ err: errorSummary(error) }, 'Requesting a pairing code failed');
        await this.#endLink('pairing_code_failed');
      }
    }
  }

  async #onOpen(socket: WASocket): Promise<void> {
    const wasLinking = this.#linking !== null;
    this.#clearLinking();
    this.#reconnectAttempts = 0;
    this.#phoneNumber = phoneFromJid(socket.user?.id);
    if (wasLinking) {
      await this.#store.markLinked(this.accountId);
    }

    this.#setStatus('connected', wasLinking ? 'linked' : undefined, socket.user?.name ?? undefined);
  }

  async #onClose(code: number | undefined): Promise<void> {
    this.#socket = null;
    if (this.#stopped) {
      return;
    }

    this.#log.info({ code }, 'WhatsApp connection closed');

    switch (code) {
      case DisconnectReason.restartRequired:
        // Normal right after scanning the QR: WhatsApp wants a fresh connection with the new login.
        await this.#connect();
        return;

      case DisconnectReason.loggedOut:
      case DisconnectReason.badSession:
      case DisconnectReason.multideviceMismatch:
        this.#stop();
        await this.#store.clear(this.accountId);
        this.#phoneNumber = null;
        if (this.#linking) {
          this.#clearLinking();
          this.#setStatus('not_linked', 'link_failed');
        } else {
          this.#setStatus('logged_out', 'logged_out');
        }

        return;

      case DisconnectReason.forbidden:
        this.#stop();
        this.#setStatus('restricted', 'forbidden');
        return;

      case DisconnectReason.connectionReplaced:
        this.#stop();
        this.#setStatus('disconnected', 'replaced');
        return;

      default:
        if (this.#linking) {
          // No QR ever arrived: WhatsApp could not be reached (network, firewall or proxy).
          // Otherwise the QR codes ran out.
          await this.#endLink(this.#linking.reachedWhatsApp ? 'link_timeout' : 'cannot_reach_whatsapp');
          return;
        }

        this.#scheduleReconnect();
    }
  }

  #scheduleReconnect(): void {
    const delay = Math.min(2_000 * 2 ** this.#reconnectAttempts, MAX_BACKOFF_MS);
    this.#reconnectAttempts++;
    this.#setStatus('reconnecting');
    this.#log.info({ delayMs: delay }, 'Connection lost; reconnecting');
    this.#reconnectTimer = setTimeout(() => {
      this.#reconnectTimer = null;
      if (!this.#stopped) {
        this.#connect().catch((error: unknown) => {
          this.#log.error({ err: errorSummary(error) }, 'Reconnect failed');
          this.#scheduleReconnect();
        });
      }
    }, delay);
  }

  async #linkTimedOut(): Promise<void> {
    if (this.#linking) {
      await this.#endLink('link_timeout');
    }
  }

  async #endLink(reason: string): Promise<void> {
    const socket = this.#socket;
    this.#stop();
    socket?.end(undefined);
    this.#clearLinking();
    await this.#store.clear(this.accountId);
    this.#setStatus('not_linked', reason);
  }

  #clearLinking(): void {
    this.#linking = null;
    if (this.#linkTimer) {
      clearTimeout(this.#linkTimer);
      this.#linkTimer = null;
    }
  }

  #stop(): void {
    this.#stopped = true;
    this.#socket = null;
    if (this.#reconnectTimer) {
      clearTimeout(this.#reconnectTimer);
      this.#reconnectTimer = null;
    }

    if (this.#linkTimer) {
      clearTimeout(this.#linkTimer);
      this.#linkTimer = null;
    }
  }

  #setStatus(status: ConnectionStatus, reason?: string, name?: string): void {
    const changed = status !== this.#status;
    this.#status = status;
    if (changed || reason) {
      this.#log.info({ status, reason }, 'WhatsApp connection status');
      this.#emit({
        type: 'connection',
        accountId: this.accountId,
        status,
        ...(reason ? { reason } : {}),
        ...(this.#phoneNumber && status === 'connected' ? { phoneNumber: this.#phoneNumber } : {}),
        ...(name ? { name } : {}),
      });
    }
  }
}

export function mapMessageStatus(status: number): MessageStatus | null {
  switch (status) {
    case WAMessageStatus.SERVER_ACK:
      return 'sent';
    case WAMessageStatus.DELIVERY_ACK:
      return 'delivered';
    case WAMessageStatus.READ:
    case WAMessageStatus.PLAYED:
      return 'read';
    case WAMessageStatus.ERROR:
      return 'failed';
    default:
      return null;
  }
}

/** Error name and message only: Baileys errors can carry whole stanzas with phone numbers. */
export function errorSummary(error: unknown): string {
  return error instanceof Error ? `${error.name}: ${error.message}` : 'unknown error';
}
