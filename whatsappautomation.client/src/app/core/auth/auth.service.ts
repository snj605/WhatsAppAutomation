import { HttpClient } from '@angular/common/http';
import { computed, inject, Injectable, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, finalize, firstValueFrom, map, Observable, of, shareReplay } from 'rxjs';
import { Me, UserInfo, WebSession } from './auth.models';

const WEB_AUTH = '/api/v1/auth/web';

/**
 * Who is signed in, and the access token for API calls.
 *
 * - The access token (10 minutes) lives only in memory, never in localStorage.
 * - The refresh token lives in an HttpOnly cookie that page scripts cannot read; the browser
 *   sends it to /api/v1/auth/web/* only. Reloading the page restores the session from that cookie.
 * - State is held in signals, so templates update by themselves when it changes.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private accessToken: string | null = null;
  private accessTokenExpiresAt = 0;
  private refreshInFlight: Observable<string | null> | null = null;

  private readonly currentUser = signal<UserInfo | null>(null);
  private readonly permissionSet = signal<ReadonlySet<string>>(new Set());

  readonly user = this.currentUser.asReadonly();
  readonly isSignedIn = computed(() => this.currentUser() !== null);

  get token(): string | null {
    return this.accessToken;
  }

  can(permission: string): boolean {
    return this.permissionSet().has(permission);
  }

  /** Runs once at start-up. Never throws: a failed restore just means "signed out". */
  async restoreSession(): Promise<void> {
    try {
      await firstValueFrom(this.http.get(`${WEB_AUTH}/xsrf`));
      const token = await firstValueFrom(this.refresh());
      if (token) {
        await this.loadMe();
      }
    } catch {
      this.clear();
    }
  }

  async signIn(userName: string, password: string): Promise<void> {
    const session = await firstValueFrom(this.http.post<WebSession>(`${WEB_AUTH}/login`, { userName, password }));
    this.keep(session);
    await this.loadMe();
  }

  async signOut(): Promise<void> {
    try {
      await firstValueFrom(this.http.post(`${WEB_AUTH}/logout`, null));
    } finally {
      this.clear();
      await this.router.navigateByUrl('/login');
    }
  }

  /**
   * Gets a new access token using the refresh cookie. If several requests need a refresh at once,
   * they all share one call: the server ends the session if the same refresh token is used twice.
   */
  refresh(): Observable<string | null> {
    this.refreshInFlight ??= this.http.post<WebSession>(`${WEB_AUTH}/refresh`, null).pipe(
      map((session) => {
        this.keep(session);
        this.currentUser.set(session.user);
        return session.accessToken;
      }),
      catchError(() => {
        this.clear();
        return of(null);
      }),
      finalize(() => (this.refreshInFlight = null)),
      shareReplay(1),
    );
    return this.refreshInFlight;
  }

  /**
   * A token that is valid for at least another 30 seconds, renewing it first if needed. For
   * connections that cannot go through the HTTP interceptor, such as SignalR's WebSocket.
   */
  async freshToken(): Promise<string> {
    if (this.accessToken && this.accessTokenExpiresAt - Date.now() > 30_000) {
      return this.accessToken;
    }

    return (await firstValueFrom(this.refresh())) ?? '';
  }

  /** Called when the session cannot be renewed: forget everything and go to the sign-in page. */
  sessionEnded(returnUrl: string): void {
    this.clear();
    void this.router.navigate(['/login'], { queryParams: { returnUrl } });
  }

  private async loadMe(): Promise<void> {
    const me = await firstValueFrom(this.http.get<Me>('/api/v1/auth/me'));
    this.currentUser.set(me.user);
    this.permissionSet.set(new Set(me.permissions));
  }

  private keep(session: WebSession): void {
    this.accessToken = session.accessToken;
    this.accessTokenExpiresAt = Date.parse(session.accessTokenExpiresAt);
  }

  private clear(): void {
    this.accessToken = null;
    this.accessTokenExpiresAt = 0;
    this.currentUser.set(null);
    this.permissionSet.set(new Set());
  }
}
