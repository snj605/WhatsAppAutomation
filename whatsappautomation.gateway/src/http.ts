import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http';
import type { Logger } from 'pino';
import { secretsEqual } from './crypto.ts';
import { type MessageFile, parseFile } from './media.ts';
import { errorSummary, GatewayError, type LinkMethod } from './session.ts';

/** What the HTTP layer needs from the session manager (an interface, so tests can pass a fake). */
export interface Sessions {
  status(accountId: string): unknown;
  link(accountId: string, method: LinkMethod, phoneNumber: string | null): Promise<void>;
  unlink(accountId: string): Promise<void>;
  sendText(accountId: string, to: string, text: string, reference: string, file?: MessageFile | null): Promise<string>;
  checkNumbers(accountId: string, numbers: string[]): Promise<NumberCheck[]>;
}

/** Whether one number has WhatsApp. */
export interface NumberCheck {
  number: string;
  onWhatsApp: boolean;
}

/** Most numbers one check may ask about (WhatsApp is asked once per call). */
export const MAX_NUMBERS_PER_CHECK = 50;

const MAX_BODY_BYTES = 64 * 1024;
const MAX_TEXT_LENGTH = 4096;
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const ROUTE = /^\/accounts\/([^/]+)\/(status|link|unlink|messages|check-numbers)$/;

/**
 * The gateway's small HTTP API. Only the WhatsApp Automation API calls it, with the shared secret
 * in X-Gateway-Secret. Every route except /health requires it.
 *
 *   GET  /health
 *   GET  /accounts/{id}/status
 *   POST /accounts/{id}/link       { method: "qr" | "code", phoneNumber?: "+91…" }
 *   POST /accounts/{id}/unlink
 *   POST /accounts/{id}/messages   { to: "+91…", text, reference, file?: { id, name, mimeType } }  →  { messageId }
 *   POST /accounts/{id}/check-numbers { numbers: ["+91…", …] (1–50) }  →  { results: [{ number, onWhatsApp }] }
 */
export function createHttpServer(sessions: Sessions, secret: string, log: Logger): Server {
  return createServer((request, response) => {
    handle(request, response).catch((error: unknown) => {
      if (error instanceof GatewayError) {
        send(response, error.status, { code: error.code, message: error.message });
        return;
      }

      log.error({ err: errorSummary(error) }, 'Request failed');
      send(response, 500, { code: 'gateway_error', message: 'The gateway could not complete the request.' });
    });
  });

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const path = new URL(request.url ?? '/', 'http://gateway').pathname;
    if (request.method === 'GET' && path === '/health') {
      send(response, 200, { ok: true });
      return;
    }

    const provided = request.headers['x-gateway-secret'];
    if (typeof provided !== 'string' || !secretsEqual(provided, secret)) {
      send(response, 401, { code: 'unauthorized', message: 'Missing or wrong gateway secret.' });
      return;
    }

    const match = ROUTE.exec(path);
    const accountId = match?.[1];
    const action = match?.[2];
    if (!accountId || !action) {
      send(response, 404, { code: 'not_found', message: 'Unknown route.' });
      return;
    }

    if (!UUID.test(accountId)) {
      throw new GatewayError('invalid_account', 400, 'The account id must be a UUID.');
    }

    const expected = action === 'status' ? 'GET' : 'POST';
    if (request.method !== expected) {
      send(response, 405, { code: 'method_not_allowed', message: `Use ${expected}.` });
      return;
    }

    switch (action) {
      case 'status':
        send(response, 200, sessions.status(accountId));
        return;

      case 'link': {
        const body = await readJson(request);
        const method = body['method'];
        if (method !== 'qr' && method !== 'code') {
          throw new GatewayError('invalid_method', 400, 'method must be "qr" or "code".');
        }

        const phoneNumber = typeof body['phoneNumber'] === 'string' ? body['phoneNumber'] : null;
        await sessions.link(accountId, method, phoneNumber);
        send(response, 202, sessions.status(accountId));
        return;
      }

      case 'unlink':
        await sessions.unlink(accountId);
        send(response, 200, sessions.status(accountId));
        return;

      case 'messages': {
        const body = await readJson(request);
        const { to, text, reference } = body;
        if (typeof to !== 'string' || typeof reference !== 'string' || !reference) {
          throw new GatewayError('invalid_message', 400, '"to" and "reference" are required.');
        }

        if (typeof text !== 'string' || text.trim() === '' || text.length > MAX_TEXT_LENGTH) {
          throw new GatewayError('invalid_message', 400, `"text" is required, at most ${MAX_TEXT_LENGTH} characters.`);
        }

        let file: MessageFile | null;
        try {
          file = parseFile(body['file']);
        } catch (error) {
          throw new GatewayError('invalid_message', 400, error instanceof Error ? error.message : 'Invalid file.');
        }

        const messageId = await sessions.sendText(accountId, to, text, reference, file);
        send(response, 200, { messageId });
        return;
      }

      case 'check-numbers': {
        const body = await readJson(request);
        const numbers = body['numbers'];
        if (
          !Array.isArray(numbers) ||
          numbers.length === 0 ||
          numbers.length > MAX_NUMBERS_PER_CHECK ||
          !numbers.every((n) => typeof n === 'string')
        ) {
          throw new GatewayError('invalid_numbers', 400, `"numbers" must be a list of 1 to ${MAX_NUMBERS_PER_CHECK} phone numbers.`);
        }

        const results = await sessions.checkNumbers(accountId, [...new Set(numbers as string[])]);
        send(response, 200, { results });
        return;
      }
    }
  }
}

async function readJson(request: IncomingMessage): Promise<Record<string, unknown>> {
  const chunks: Buffer[] = [];
  let size = 0;
  for await (const chunk of request) {
    size += (chunk as Buffer).length;
    if (size > MAX_BODY_BYTES) {
      throw new GatewayError('body_too_large', 413, 'Request body is too large.');
    }

    chunks.push(chunk as Buffer);
  }

  try {
    const value: unknown = JSON.parse(Buffer.concat(chunks).toString('utf8') || '{}');
    if (typeof value === 'object' && value !== null && !Array.isArray(value)) {
      return value as Record<string, unknown>;
    }
  } catch {
    // fall through
  }

  throw new GatewayError('invalid_json', 400, 'The body must be a JSON object.');
}

function send(response: ServerResponse, status: number, body: unknown): void {
  const json = JSON.stringify(body);
  response.writeHead(status, {
    'content-type': 'application/json; charset=utf-8',
    'content-length': Buffer.byteLength(json),
    'cache-control': 'no-store',
  });
  response.end(json);
}
