import assert from 'node:assert/strict';
import { test } from 'node:test';
import { typingDelayMs } from '../src/human.ts';

test('typingDelayMs stays between 1.5 and 8 seconds', () => {
  assert.equal(typingDelayMs('hi', () => 0.5), 1_500);
  assert.equal(typingDelayMs('x'.repeat(1000), () => 0.5), 8_000);
  assert.equal(typingDelayMs('x'.repeat(50), () => 0.5), 3_000);
});

test('typingDelayMs varies with the random number', () => {
  const low = typingDelayMs('x'.repeat(50), () => 0);
  const high = typingDelayMs('x'.repeat(50), () => 0.999);
  assert.ok(low < high);
  assert.ok(low >= 1_500 && high <= 8_000);
});
