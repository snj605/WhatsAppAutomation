import assert from 'node:assert/strict';
import { test } from 'node:test';
import { DisconnectReason } from 'baileys';
import { closeReason, withTimeout } from '../src/session.ts';

test('close reasons are short and readable', () => {
  assert.equal(closeReason(DisconnectReason.connectionLost), 'connection_lost');
  assert.equal(closeReason(DisconnectReason.badSession), 'session_problem');
  assert.equal(closeReason(undefined), 'network_error');
  assert.equal(closeReason(499), 'closed_499');
});

test('withTimeout rejects a call that never answers', async () => {
  await assert.rejects(withTimeout(new Promise(() => undefined), 20), /Timed out/);
  assert.equal(await withTimeout(Promise.resolve(7), 20), 7);
});

test('the QR is also sent as a ready-made picture for the app', async () => {
  const { qrImage } = await import('../src/session.ts');
  const image = await qrImage('2@abc,def,ghi');
  assert.ok(image?.startsWith('data:image/png;base64,'));
  assert.ok((image ?? '').length < 20_000);
});
