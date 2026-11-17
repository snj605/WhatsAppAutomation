import { readFileSync } from 'node:fs';

export interface GatewayConfig {
  /** Address to listen on. Only the API may call the gateway, so the default is this machine only. */
  host: string;
  port: number;
  /** Shared secret the API sends in the X-Gateway-Secret header, and the gateway sends back with events. */
  secret: string;
  /** 32-byte AES-256-GCM key for the saved WhatsApp login. Losing it means linking again. */
  authKey: Buffer;
  /** Where connection changes, QR codes and message statuses are sent. */
  eventsUrl: string;
  logLevel: string;
  /** Baileys' own log. Off by default because it can contain phone numbers. */
  baileysLogLevel: string;
  /** Show "typing…" to the recipient for a few seconds before each message. */
  simulateTyping: boolean;
}

/**
 * Reads settings from environment variables. PostgreSQL uses the standard PGHOST, PGPORT,
 * PGDATABASE, PGUSER and PGPASSWORD variables, which the "pg" package reads by itself.
 */
export function loadConfig(env: NodeJS.ProcessEnv = process.env): GatewayConfig {
  const secret = required(env, 'GATEWAY_SECRET');
  if (secret.length < 32) {
    throw new Error('GATEWAY_SECRET must be at least 32 characters.');
  }

  const keyText = env['GATEWAY_AUTH_KEY_FILE']
    ? readFileSync(env['GATEWAY_AUTH_KEY_FILE'], 'utf8').trim()
    : required(env, 'GATEWAY_AUTH_KEY');
  const authKey = Buffer.from(keyText, 'base64');
  if (authKey.length !== 32) {
    throw new Error('GATEWAY_AUTH_KEY must be 32 bytes in base64 (44 characters).');
  }

  const port = Number(env['GATEWAY_PORT'] ?? '5180');
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error('GATEWAY_PORT must be a port number.');
  }

  return {
    host: env['GATEWAY_HOST'] ?? '127.0.0.1',
    port,
    secret,
    authKey,
    eventsUrl: required(env, 'GATEWAY_EVENTS_URL'),
    logLevel: env['LOG_LEVEL'] ?? 'info',
    baileysLogLevel: env['BAILEYS_LOG_LEVEL'] ?? 'silent',
    simulateTyping: (env['GATEWAY_SIMULATE_TYPING'] ?? 'true').toLowerCase() !== 'false',
  };
}

function required(env: NodeJS.ProcessEnv, name: string): string {
  const value = env[name];
  if (!value) {
    throw new Error(`${name} is not set.`);
  }

  return value;
}
