import type { Logger } from 'pino';

/** What the gateway tells the API. The API forwards QR codes and pairing codes live to the app. */
export type GatewayEvent =
  /** image: the same QR drawn as a PNG data URL, for apps that cannot draw it themselves (the mobile app). */
  | { type: 'qr'; accountId: string; qr: string; image?: string }
  | { type: 'pairing_code'; accountId: string; code: string }
  | {
      type: 'connection';
      accountId: string;
      status: ConnectionStatus;
      /** Why it changed, as a short code, e.g. "logged_out" or "link_timeout". */
      reason?: string;
      phoneNumber?: string;
      name?: string;
    }
  | { type: 'message_status'; accountId: string; reference: string; status: MessageStatus; at: string };

export type ConnectionStatus =
  | 'not_linked'
  | 'linking'
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'logged_out'
  | 'restricted'
  | 'disconnected';

export type MessageStatus = 'sent' | 'delivered' | 'read' | 'failed';

export type EventSink = (event: GatewayEvent) => void;

const RETRY_DELAYS_MS = [1_000, 2_000, 4_000, 8_000, 16_000, 30_000];

/**
 * Posts events to the API in order, one account at a time. If the API is down (for example
 * still starting), each event is retried for about a minute. QR codes are not retried: a new one
 * arrives every 20 seconds anyway and an old one is useless.
 */
export function createHttpEventSink(url: string, secret: string, log: Logger): EventSink {
  const queues = new Map<string, Promise<void>>();

  return (event) => {
    const previous = queues.get(event.accountId) ?? Promise.resolve();
    const next = previous.then(() => deliver(event)).catch(() => undefined);
    queues.set(event.accountId, next);
  };

  async function deliver(event: GatewayEvent): Promise<void> {
    const attempts = event.type === 'qr' ? 1 : RETRY_DELAYS_MS.length + 1;
    let lastProblem = '';
    for (let attempt = 0; attempt < attempts; attempt++) {
      try {
        const response = await fetch(url, {
          method: 'POST',
          headers: { 'content-type': 'application/json', 'x-gateway-secret': secret },
          body: JSON.stringify(event),
          signal: AbortSignal.timeout(10_000),
        });
        if (response.ok) {
          return;
        }

        // 4xx means the API understood and refused; trying again will not help.
        if (response.status >= 400 && response.status < 500) {
          log.warn({ type: event.type, status: response.status }, 'API refused an event');
          return;
        }

        lastProblem = `HTTP ${response.status}`;
      } catch (error) {
        // API not reachable yet; retry below.
        lastProblem = describe(error);
      }

      const delay = RETRY_DELAYS_MS[attempt];
      if (delay === undefined) {
        break;
      }

      await new Promise((resolve) => setTimeout(resolve, delay));
    }

    log.error({ type: event.type, url, problem: lastProblem }, 'Could not deliver an event to the API');
  }
}

/** A short reason such as "ECONNREFUSED" or "TimeoutError", without the event's contents. */
function describe(error: unknown): string {
  if (!(error instanceof Error)) {
    return String(error);
  }

  const cause = error.cause as { code?: string; message?: string } | undefined;
  return [error.name, cause?.code ?? cause?.message].filter(Boolean).join(': ');
}
