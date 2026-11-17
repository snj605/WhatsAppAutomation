import pg from 'pg';
import { pino } from 'pino';
import { AuthStore } from './auth-store.ts';
import { loadConfig } from './config.ts';
import { createHttpEventSink } from './events.ts';
import { createFileLoader } from './files.ts';
import { createHttpServer } from './http.ts';
import { SessionManager } from './session-manager.ts';
import { errorSummary } from './session.ts';

// Entry point: "npm start". In development the API starts this for you (F5); see the README.

const config = loadConfig();
const log = pino({ level: config.logLevel, base: { service: 'gateway' } });
const baileysLog = pino({ level: config.baileysLogLevel, base: { service: 'baileys' } });

const pool = new pg.Pool({ max: 5 });
pool.on('error', (error) => log.error({ err: errorSummary(error) }, 'PostgreSQL connection error'));

const store = new AuthStore(pool, config.authKey);
await store.ensureTableExists();

const emit = createHttpEventSink(config.eventsUrl, config.secret, log);
const sessions = new SessionManager(store, emit, log, baileysLog, {
  simulateTyping: config.simulateTyping,
  loadFile: createFileLoader(config.eventsUrl, config.secret),
});
const server = createHttpServer(sessions, config.secret, log);

server.listen(config.port, config.host, () => {
  log.info({ host: config.host, port: config.port }, 'Gateway listening');
});

await sessions.resumeAll();

let stopping = false;
const stop = (): void => {
  if (stopping) {
    return;
  }

  stopping = true;
  log.info('Stopping');
  sessions.shutdown();
  server.close();
  void pool.end().finally(() => process.exit(0));
  setTimeout(() => process.exit(0), 5_000).unref();
};

process.on('SIGINT', stop);
process.on('SIGTERM', stop);
// The API starts the gateway in development and closes our stdin when it stops: stop with it.
if (process.env['GATEWAY_EXIT_ON_STDIN_CLOSE'] === 'true') {
  process.stdin.on('end', stop);
  process.stdin.resume();
}
