import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../auth/auth.service';

/**
 * Runs for every HTTP call:
 * 1. Adds "Authorization: Bearer <access token>" to API calls.
 * 2. If the API answers 401 (token expired), renews the token once and repeats the call.
 * 3. If renewing fails, the session is over: go to the sign-in page.
 * Calls to /api/v1/auth/web/* are left alone; they use the refresh cookie instead.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith('/api/') || request.url.startsWith('/api/v1/auth/web/')) {
    return next(request);
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  return next(withToken(request, auth.token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }

      return auth.refresh().pipe(
        switchMap((token) => {
          if (!token) {
            auth.sessionEnded(router.url);
            return throwError(() => error);
          }

          return next(withToken(request, token));
        }),
      );
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
