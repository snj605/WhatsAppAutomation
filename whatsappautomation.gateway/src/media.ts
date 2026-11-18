import type { proto } from 'baileys';

/** A file to send with a message, as the API describes it. The bytes are fetched separately, once. */
export interface MessageFile {
  id: string;
  name: string;
  mimeType: string;
  /** Send as a document (a file to download) even if it is a photo or video: for videos above WhatsApp's video size. */
  asDocument?: boolean;
}

export type MediaKind = 'image' | 'video' | 'document';

/**
 * How WhatsApp should show the file: photos and MP4 videos inline (with the text as caption), everything else as
 * a document with its file name. GIF, HEIC, MOV and the like go as documents so they are never re-encoded.
 */
export function mediaKind(mimeType: string): MediaKind {
  const type = mimeType.toLowerCase();
  if (type === 'image/jpeg' || type === 'image/png' || type === 'image/webp') return 'image';
  if (type === 'video/mp4') return 'video';
  return 'document';
}

/**
 * The uploaded media message (same file for everyone) with this person's text as its caption. The upload itself
 * (url, mediaKey, directPath) is reused, so the file is uploaded to WhatsApp only once.
 */
export function withCaption(prepared: proto.IMessage, caption: string): proto.IMessage {
  if (prepared.imageMessage) return { imageMessage: { ...prepared.imageMessage, caption } };
  if (prepared.videoMessage) return { videoMessage: { ...prepared.videoMessage, caption } };
  if (prepared.documentMessage) return { documentMessage: { ...prepared.documentMessage, caption } };
  throw new Error('Unexpected media message.');
}

/** Valid file reference from the HTTP body, or null when there is none. Throws on a malformed one. */
export function parseFile(value: unknown): MessageFile | null {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'object') throw new TypeError('file must be an object.');
  const { id, name, mimeType, asDocument } = value as Record<string, unknown>;
  const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
  if (typeof id !== 'string' || !uuid.test(id)) throw new TypeError('file.id must be a UUID.');
  if (typeof name !== 'string' || name.length === 0 || name.length > 255) throw new TypeError('file.name is required.');
  if (typeof mimeType !== 'string' || !mimeType.includes('/') || mimeType.length > 127) throw new TypeError('file.mimeType is required.');
  if (asDocument !== undefined && typeof asDocument !== 'boolean') throw new TypeError('file.asDocument must be true or false.');
  return { id, name, mimeType, asDocument: asDocument === true };
}

/** How long an upload to WhatsApp is reused before uploading the file again (WhatsApp links do not last forever). */
export const MEDIA_REUSE_MS = 12 * 60 * 60 * 1000;
