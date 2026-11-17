import assert from 'node:assert/strict';
import { test } from 'node:test';
import { phoneFromJid, toDigits } from '../src/phone.ts';
import { mapMessageStatus } from '../src/session.ts';

test('toDigits accepts E.164 only', () => {
  assert.equal(toDigits('+919876543210'), '919876543210');
  assert.equal(toDigits('919876543210'), null);
  assert.equal(toDigits('+0123456789'), null);
  assert.equal(toDigits('+91 98765 43210'), null);
  assert.equal(toDigits(''), null);
});

test('phoneFromJid reads the number from a phone JID with or without a device suffix', () => {
  assert.equal(phoneFromJid('919876543210:12@s.whatsapp.net'), '+919876543210');
  assert.equal(phoneFromJid('919876543210@s.whatsapp.net'), '+919876543210');
  assert.equal(phoneFromJid('12345@lid'), null);
  assert.equal(phoneFromJid(undefined), null);
});

test('WhatsApp message statuses map to the API statuses', () => {
  assert.equal(mapMessageStatus(0), 'failed');
  assert.equal(mapMessageStatus(1), null);
  assert.equal(mapMessageStatus(2), 'sent');
  assert.equal(mapMessageStatus(3), 'delivered');
  assert.equal(mapMessageStatus(4), 'read');
  assert.equal(mapMessageStatus(5), 'read');
});
