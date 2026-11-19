import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { problemMessage } from '../../core/http/problem';

/**
 * Before an important change (sending hours, an occasion's automatic sending, deleting) the user types their
 * password again, so nothing changes by a slip while scrolling. The app uses fingerprint or face instead.
 * ask() opens the one dialog of the website and resolves true once the password is right, false on Cancel.
 */
@Injectable({ providedIn: 'root' })
export class IdentityCheck {
  private readonly http = inject(HttpClient);
  private resolve: ((ok: boolean) => void) | null = null;

  /** What is being confirmed, shown in the dialog; null while closed. */
  readonly reason = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ask(reason: string): Promise<boolean> {
    this.resolve?.(false);
    this.error.set(null);
    this.busy.set(false);
    this.reason.set(reason);
    return new Promise<boolean>(resolve => (this.resolve = resolve));
  }

  async check(password: string): Promise<void> {
    if (!password) {
      this.error.set('Type your password.');
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    try {
      await firstValueFrom(this.http.post('/api/v1/auth/confirm-password', { password }));
      this.finish(true);
    } catch (e) {
      this.error.set(problemMessage(e));
      this.busy.set(false);
    }
  }

  cancel(): void {
    this.finish(false);
  }

  private finish(ok: boolean): void {
    this.reason.set(null);
    this.busy.set(false);
    const resolve = this.resolve;
    this.resolve = null;
    resolve?.(ok);
  }
}
