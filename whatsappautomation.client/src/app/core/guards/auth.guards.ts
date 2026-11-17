import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/** Pages behind sign-in. Not signed in → the sign-in page, which brings you back afterwards. */
export const signedInGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isSignedIn() || inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** The sign-in page itself: already signed in → Home. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return !auth.isSignedIn() || inject(Router).createUrlTree(['/']);
};

/**
 * Pages that need a permission (any one of those listed). This only hides what you cannot use; the server checks the
 * permission again on every request, so the real protection is there.
 */
export function permissionGuard(...anyOf: string[]): CanActivateFn {
  return () => {
    const auth = inject(AuthService);
    return anyOf.some((p) => auth.can(p)) || inject(Router).createUrlTree(['/home']);
  };
}
