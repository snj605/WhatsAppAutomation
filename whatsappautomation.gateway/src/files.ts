import { GatewayError } from './session.ts';

const MAX_FILE_BYTES = 16 * 1024 * 1024;

/**
 * Fetches a file's bytes from the API's /internal/files/{id} (same host as the events URL, same shared secret).
 * Called once per broadcast file; the upload to WhatsApp is then reused for every recipient.
 */
export function createFileLoader(eventsUrl: string, secret: string): (fileId: string) => Promise<Buffer> {
  const base = new URL('/internal/files/', eventsUrl);
  return async (fileId) => {
    const response = await fetch(new URL(encodeURIComponent(fileId), base), {
      headers: { 'x-gateway-secret': secret },
      signal: AbortSignal.timeout(60_000),
    });
    if (!response.ok) {
      throw new GatewayError('file_unavailable', response.status === 404 ? 422 : 502, 'The file to send could not be fetched from the app.');
    }

    const data = Buffer.from(await response.arrayBuffer());
    if (data.length === 0 || data.length > MAX_FILE_BYTES) {
      throw new GatewayError('file_unavailable', 422, 'The file to send is empty or larger than 16 MB.');
    }

    return data;
  };
}
