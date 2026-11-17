import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import Aura from '@primeuix/themes/aura';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { PRIMENG_LICENSE_KEY } from './core/config/primeng-license';
import { authInterceptor } from './core/interceptors/auth.interceptor';

/**
 * Everything the app needs, registered once at start-up (the standalone replacement for AppModule).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      // Angular copies the XSRF-TOKEN cookie into the X-XSRF-TOKEN header on POST/PUT/DELETE.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([authInterceptor]),
    ),
    providePrimeNG({
      theme: { preset: Aura, options: { darkModeSelector: false } },
      license: PRIMENG_LICENSE_KEY || undefined,
    }),
    MessageService,
    ConfirmationService,
    // Before the first page shows: get an XSRF token and restore the session if the browser has one.
    provideAppInitializer(() => inject(AuthService).restoreSession()),
  ],
};
