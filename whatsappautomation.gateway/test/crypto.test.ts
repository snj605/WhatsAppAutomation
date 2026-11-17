import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { test } from 'node:test';
import { decrypt, encrypt, secretsEqual } from '../src/crypto.ts';

const key = randomBytes(32);

test('encrypt then decrypt gives the original bytes', () => {
  const plain = Buffer.from('{"noiseKey":"secret"}');
  const blob = encrypt(key, plain, 'a|creds|');
  assert.notDeepEqual(blob.subarray(28), plain);
  assert.deepEqual(decrypt(key, blob, 'a|creds|'), plain);
});

test('the same value encrypts differently each time (random IV)', () => {
  const plain = Buffer.from('same');
  assert.notDeepEqual(encrypt(key, plain, 'c'), encrypt(key, plain, 'c'));
});

test('a row moved to another account or key id does not decrypt', () => {
  const blob = encrypt(key, Buffer.from('x'), 'account-1|session|1');
  assert.throws(() => decrypt(key, blob, 'account-2|session|1'));
});

test('a changed byte or a wrong key is detected', () => {
  const blob = encrypt(key, Buffer.from('hello'), 'c');
  const tampered = Buffer.from(blob);
  tampered.writeUInt8(tampered.readUInt8(tampered.length - 1) ^ 1, tampered.length - 1);
  assert.throws(() => decrypt(key, tampered, 'c'));
  assert.throws(() => decrypt(randomBytes(32), blob, 'c'));
});

test('secretsEqual compares secrets of any length', () => {
  assert.equal(secretsEqual('abc', 'abc'), true);
  assert.equal(secretsEqual('abc', 'abd'), false);
  assert.equal(secretsEqual('abc', 'abcd'), false);
});
