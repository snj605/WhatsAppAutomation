import { HttpErrorResponse } from '@angular/common/http';

/**
 * The server answers errors with RFC 9457 "problem details": { title, status, detail, code, errors }.
 * This turns any HTTP error into one sentence a person can read.
 */
export function problemMessage(error: unknown, fallback = 'Something went wrong. Please try again.'): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (error.status === 0) {
    return 'Cannot reach the server. Check that it is running.';
  }

  const body = error.error as { detail?: string; title?: string; errors?: Record<string, string[]> } | null;
  const validation = body?.errors ? Object.values(body.errors).flat() : [];
  if (validation.length > 0) {
    return validation.join(' ');
  }

  return body?.detail ?? body?.title ?? fallback;
}

/** The stable error code (for example "users.last_owner"), when the server sent one. */
export function problemCode(error: unknown): string | undefined {
  return error instanceof HttpErrorResponse ? (error.error as { code?: string } | null)?.code : undefined;
}
