import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import type { AddressInfo } from 'node:net';
import { after, before, test } from 'node:test';
import { pino } from 'pino';
import { createHttpServer, type Sessions } from '../src/http.ts';
import { GatewayError } from '../src/session.ts';

// Made fresh for each run, so no secret-looking value is kept in git.
const secret = randomBytes(32).toString('base64url');
const accountId = '0b7c2f1e-1d2a-4c3b-9e8f-7a6b5c4d3e2f';
const calls: string[] = [];

const fake: Sessions = {
  status: (id) => ({ accountId: id, status: 'connected', phoneNumber: '+911234567890' }),
  link: async (id, method, phone) => void calls.push(`link ${id} ${method} ${phone}`),
  unlink: async (id) => {
    calls.push(`unlink ${id}`);
    return true;
  },
  sendText: async (_id, to) => {
    if (to === '+15550000000') {
      throw new GatewayError('not_on_whatsapp', 422, 'This number is not on WhatsApp.');
    }
    return 'WAMSGID1';
  },
  checkNumbers: async (_id, numbers) => numbers.map((number) => ({ number, onWhatsApp: number !== '+15550000000' })),
};

const server = createHttpServer(fake, secret, pino({ level: 'silent' }));
let base = '';

before(async () => {
  await new Promise<void>((resolve) => server.listen(0, '127.0.0.1', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});
after(() => server.close());

const call = (path: string, init: RequestInit = {}, withSecret = true) =>
  fetch(base + path, {
    ...init,
    headers: { 'content-type': 'application/json', ...(withSecret ? { 'x-gateway-secret': secret } : {}) },
  });

test('health needs no secret', async () => {
  assert.equal((await call('/health', {}, false)).status, 200);
});

test('every other route needs the secret', async () => {
  assert.equal((await call(`/accounts/${accountId}/status`, {}, false)).status, 401);
  const wrong = await fetch(`${base}/accounts/${accountId}/status`, { headers: { 'x-gateway-secret': 'nope' } });
  assert.equal(wrong.status, 401);
});

test('status, link and unlink', async () => {
  const status = await call(`/accounts/${accountId}/status`);
  assert.equal(status.status, 200);
  assert.equal(((await status.json()) as { status: string }).status, 'connected');

  const link = await call(`/accounts/${accountId}/link`, { method: 'POST', body: JSON.stringify({ method: 'code', phoneNumber: '+911234567890' }) });
  assert.equal(link.status, 202);
  const unlinked = await call(`/accounts/${accountId}/unlink`, { method: 'POST' });
  assert.equal(unlinked.status, 200);
  assert.equal(((await unlinked.json()) as { removedFromPhone: boolean }).removedFromPhone, true);
  assert.deepEqual(calls, [`link ${accountId} code +911234567890`, `unlink ${accountId}`]);
});

test('bad input is rejected with a code', async () => {
  const badId = await call('/accounts/not-a-uuid/status');
  assert.equal(badId.status, 400);
  const badMethod = await call(`/accounts/${accountId}/link`, { method: 'POST', body: JSON.stringify({ method: 'sms' }) });
  assert.equal(((await badMethod.json()) as { code: string }).code, 'invalid_method');
  const badJson = await call(`/accounts/${accountId}/link`, { method: 'POST', body: '[1]' });
  assert.equal(badJson.status, 400);
  const wrongVerb = await call(`/accounts/${accountId}/link`);
  assert.equal(wrongVerb.status, 405);
  const empty = await call(`/accounts/${accountId}/messages`, { method: 'POST', body: JSON.stringify({ to: '+911234567890', text: ' ', reference: 'r' }) });
  assert.equal(empty.status, 400);
});

test('send returns the WhatsApp message id, or the error code', async () => {
  const ok = await call(`/accounts/${accountId}/messages`, { method: 'POST', body: JSON.stringify({ to: '+911234567890', text: 'Hi', reference: 'r1' }) });
  assert.deepEqual(await ok.json(), { messageId: 'WAMSGID1' });
  const missing = await call(`/accounts/${accountId}/messages`, { method: 'POST', body: JSON.stringify({ to: '+15550000000', text: 'Hi', reference: 'r2' }) });
  assert.equal(missing.status, 422);
  assert.equal(((await missing.json()) as { code: string }).code, 'not_on_whatsapp');
});

test('check-numbers answers per number and refuses bad lists', async () => {
  const ok = await call(`/accounts/${accountId}/check-numbers`, {
    method: 'POST',
    body: JSON.stringify({ numbers: ['+911234567890', '+15550000000', '+911234567890'] }),
  });
  assert.equal(ok.status, 200);
  assert.deepEqual(await ok.json(), {
    results: [
      { number: '+911234567890', onWhatsApp: true },
      { number: '+15550000000', onWhatsApp: false },
    ],
  });
  const empty = await call(`/accounts/${accountId}/check-numbers`, { method: 'POST', body: JSON.stringify({ numbers: [] }) });
  assert.equal(empty.status, 400);
  const tooMany = await call(`/accounts/${accountId}/check-numbers`, {
    method: 'POST',
    body: JSON.stringify({ numbers: Array.from({ length: 51 }, (_, i) => `+9198765${String(i).padStart(5, '0')}`) }),
  });
  assert.equal(tooMany.status, 400);
});

test('a body over 64 KB is refused', async () => {
  const big = await call(`/accounts/${accountId}/messages`, { method: 'POST', body: JSON.stringify({ text: 'x'.repeat(70_000) }) });
  assert.equal(big.status, 413);
});
