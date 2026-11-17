import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';

/**
 * Pages behind sign-in. Not signed in → the sign-in page, which brings you back afterwards. A password chosen by an
 * admin must be changed first, so every page leads to the change-password page until then.
 */
export const signedInGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isSignedIn()) {
    return router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
  }

  return !auth.mustChangePassword() || router.createUrlTree(['/change-password']);
};

/** The change-password page: signed in only. */
export const signedInOnlyGuard: CanActivateFn = (_route, state) =>
  inject(AuthService).isSignedIn() || inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });

/** Where "/" leads: the platform admin to Tenants, everyone else to Chats. */
export function homeRedirect(): string {
  return inject(AuthService).isPlatformAdmin() ? '/platform/tenants' : '/chats';
}

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
