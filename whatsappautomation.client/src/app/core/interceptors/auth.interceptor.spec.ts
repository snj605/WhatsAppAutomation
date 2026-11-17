import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import { authInterceptor } from './auth.interceptor';

describe('authInterceptor', () => {
  let http: HttpClient;
  let backend: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'login', children: [] }]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => backend.verify());

  async function signIn(token: string): Promise<void> {
    const done = auth.signIn('dhaval', 'pw');
    backend.expectOne('/api/v1/auth/web/login').flush({ accessToken: token, accessTokenExpiresAt: '', user: user() });
    await Promise.resolve();
    backend.expectOne('/api/v1/auth/me').flush({ user: user(), permissions: ['users.view'] });
    await done;
  }

  it('adds the access token to API calls', async () => {
    await signIn('token-1');

    const call = firstValueFrom(http.get('/api/v1/users'));
    const request = backend.expectOne('/api/v1/users');
    expect(request.request.headers.get('Authorization')).toBe('Bearer token-1');
    request.flush([]);
    await call;
  });

  it('does not send the access token to the cookie endpoints', () => {
    http.post('/api/v1/auth/web/refresh', null).subscribe({ error: () => undefined });
    const request = backend.expectOne('/api/v1/auth/web/refresh');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
  });

  it('renews an expired token once and repeats the call', async () => {
    await signIn('old');

    const call = firstValueFrom(http.get<string[]>('/api/v1/users'));
    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/web/refresh').flush({ accessToken: 'new', accessTokenExpiresAt: '', user: user() });
    const retry = backend.expectOne('/api/v1/users');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer new');
    retry.flush(['ok']);

    expect(await call).toEqual(['ok']);
  });

  it('shares one refresh between calls that fail at the same time', async () => {
    await signIn('old');

    const first = firstValueFrom(http.get('/api/v1/users'));
    const second = firstValueFrom(http.get('/api/v1/roles'));
    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/roles').flush(null, { status: 401, statusText: 'Unauthorized' });

    // The server ends the session if one refresh token is used twice, so only one refresh may go out.
    const refreshes = backend.match('/api/v1/auth/web/refresh');
    expect(refreshes.length).toBe(1);
    refreshes[0].flush({ accessToken: 'new', accessTokenExpiresAt: '', user: user() });
    backend.expectOne('/api/v1/users').flush([]);
    backend.expectOne('/api/v1/roles').flush([]);
    await Promise.all([first, second]);
  });

  it('signs out when the session cannot be renewed', async () => {
    await signIn('old');

    const call = firstValueFrom(http.get('/api/v1/users')).catch((e: unknown) => e);
    backend.expectOne('/api/v1/users').flush(null, { status: 401, statusText: 'Unauthorized' });
    backend.expectOne('/api/v1/auth/web/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });
    await call;

    expect(auth.isSignedIn()).toBe(false);
    expect(auth.token).toBeNull();
  });
});

function user() {
  return { id: '1', userName: 'dhaval', displayName: 'Dhaval', kind: 'tenant_admin', tenantName: 'Test', mustChangePassword: false };
}
