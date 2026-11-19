import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { IdentityCheck } from './identity-check';

describe('IdentityCheck', () => {
  let check: IdentityCheck;
  let backend: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    check = TestBed.inject(IdentityCheck);
    backend = TestBed.inject(HttpTestingController);
  });

  afterEach(() => backend.verify());

  it('opens with the reason and gives false on Cancel', async () => {
    const answer = check.ask('Change sending hours.');
    expect(check.reason()).toBe('Change sending hours.');
    check.cancel();
    expect(await answer).toBe(false);
    expect(check.reason()).toBeNull();
  });

  it('stays open on a wrong password and gives true once it is right', async () => {
    const answer = check.ask('Delete Birthday.');
    const wrong = check.check('nope');
    backend.expectOne('/api/v1/auth/confirm-password').flush(
      { title: 'That password is not correct.', status: 400 }, { status: 400, statusText: 'Bad Request' });
    await wrong;
    expect(check.reason()).not.toBeNull();
    expect(check.error()).toBeTruthy();

    const right = check.check('right password');
    const request = backend.expectOne('/api/v1/auth/confirm-password');
    expect(request.request.body).toEqual({ password: 'right password' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await right;
    expect(await answer).toBe(true);
  });

  it('asks for a password before calling the server', async () => {
    void check.ask('x');
    await check.check('');
    expect(check.error()).toBe('Type your password.');
  });
});
