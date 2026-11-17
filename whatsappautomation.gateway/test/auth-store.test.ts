import assert from 'node:assert/strict';
import { randomBytes, randomUUID } from 'node:crypto';
import { after, before, test } from 'node:test';
import pg from 'pg';
import { AuthStore } from '../src/auth-store.ts';

// Runs against a real, empty PostgreSQL database named in GATEWAY_TEST_PGDATABASE (connection from
// the usual PGHOST/PGUSER/PGPASSWORD variables). Skipped when it is not set. The table is created
// here with the same shape as the API migration, without the foreign key.
const database = process.env['GATEWAY_TEST_PGDATABASE'];
const pool = database ? new pg.Pool({ database, max: 3 }) : null;
const key = randomBytes(32);

before(async () => {
  await pool?.query(`CREATE TABLE IF NOT EXISTS whatsapp_auth_state (
    account_id uuid NOT NULL, kind varchar(50) NOT NULL, key_id varchar(500) NOT NULL,
    data bytea NOT NULL, updated_at timestamptz NOT NULL, PRIMARY KEY (account_id, kind, key_id))`);
});
after(async () => pool?.end());

test('saved logins are encrypted, read back, and deleted per account', { skip: !pool }, async () => {
  const store = new AuthStore(pool!, key);
  const accountId = randomUUID();
  const other = randomUUID();

  const first = await store.load(accountId);
  assert.equal(first.state.creds.registered, false);
  await first.saveCreds();
  await first.state.keys.set({ session: { 'abc.0': Buffer.from([1, 2, 3]) }, 'pre-key': { '1': { public: Buffer.alloc(32, 7), private: Buffer.alloc(32, 9) } } });

  const raw = await pool!.query<{ data: Buffer }>('SELECT data FROM whatsapp_auth_state WHERE account_id = $1', [accountId]);
  assert.equal(raw.rows.length, 3);
  for (const row of raw.rows) {
    assert.ok(!row.data.toString('utf8').includes('noiseKey'), 'stored data must not be readable');
  }

  const again = await store.load(accountId);
  assert.deepEqual(again.state.creds.noiseKey.public, first.state.creds.noiseKey.public);
  const sessions = await again.state.keys.get('session', ['abc.0', 'missing']);
  assert.deepEqual(Buffer.from(sessions['abc.0']!), Buffer.from([1, 2, 3]));
  assert.equal(sessions['missing'], undefined);

  await again.state.keys.set({ session: { 'abc.0': null } });
  assert.deepEqual(await again.state.keys.get('session', ['abc.0']), {});

  await store.markLinked(accountId);
  assert.ok((await store.listLinkedAccounts()).includes(accountId));
  assert.ok(!(await store.listLinkedAccounts()).includes(other));

  await store.clear(accountId);
  assert.ok(!(await store.listLinkedAccounts()).includes(accountId));
  const fresh = await store.load(accountId);
  assert.notDeepEqual(fresh.state.creds.noiseKey.public, first.state.creds.noiseKey.public);
});

test('a different key cannot read a saved login', { skip: !pool }, async () => {
  const accountId = randomUUID();
  const saved = await new AuthStore(pool!, key).load(accountId);
  await saved.saveCreds();

  await assert.rejects(() => new AuthStore(pool!, randomBytes(32)).load(accountId));
});
