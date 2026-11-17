import { createCipheriv, createDecipheriv, createHash, randomBytes, timingSafeEqual } from 'node:crypto';

const IV_LENGTH = 12;
const TAG_LENGTH = 16;

/**
 * AES-256-GCM. Output is iv | tag | ciphertext. The "context" is authenticated but not stored:
 * a row copied to another account or key id fails to decrypt instead of being accepted.
 */
export function encrypt(key: Buffer, plaintext: Buffer, context: string): Buffer {
  const iv = randomBytes(IV_LENGTH);
  const cipher = createCipheriv('aes-256-gcm', key, iv);
  cipher.setAAD(Buffer.from(context, 'utf8'));
  const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
  return Buffer.concat([iv, cipher.getAuthTag(), ciphertext]);
}

export function decrypt(key: Buffer, blob: Buffer, context: string): Buffer {
  if (blob.length < IV_LENGTH + TAG_LENGTH) {
    throw new Error('Encrypted value is too short.');
  }

  const decipher = createDecipheriv('aes-256-gcm', key, blob.subarray(0, IV_LENGTH));
  decipher.setAAD(Buffer.from(context, 'utf8'));
  decipher.setAuthTag(blob.subarray(IV_LENGTH, IV_LENGTH + TAG_LENGTH));
  return Buffer.concat([decipher.update(blob.subarray(IV_LENGTH + TAG_LENGTH)), decipher.final()]);
}

/** Compares two secrets in constant time, whatever their lengths. */
export function secretsEqual(a: string, b: string): boolean {
  const hashA = createHash('sha256').update(a, 'utf8').digest();
  const hashB = createHash('sha256').update(b, 'utf8').digest();
  return timingSafeEqual(hashA, hashB);
}
