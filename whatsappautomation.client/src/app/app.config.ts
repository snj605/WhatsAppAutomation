import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter, withComponentInputBinding, withInMemoryScrolling } from '@angular/router';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth.service';
import { PRIMENG_LICENSE_KEY } from './core/config/primeng-license';
import { AppPreset, applyThemeMode, savedThemeMode } from './core/config/theme';
import { authInterceptor } from './core/interceptors/auth.interceptor';

/**
 * Everything the app needs, registered once at start-up (the standalone replacement for AppModule).
 */
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // anchorScrolling: links such as Me → Message one person open the right part of a page.
    provideRouter(routes, withComponentInputBinding(), withInMemoryScrolling({ anchorScrolling: 'enabled' })),
    provideHttpClient(
      // Angular copies the XSRF-TOKEN cookie into the X-XSRF-TOKEN header on POST/PUT/DELETE.
      withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
      withInterceptors([authInterceptor]),
    ),
    providePrimeNG({
      theme: { preset: AppPreset, options: { darkModeSelector: '.app-dark' } },
      license: PRIMENG_LICENSE_KEY || undefined,
    }),
    MessageService,
    ConfirmationService,
    // Before the first page shows: get an XSRF token and restore the session if the browser has one.
    provideAppInitializer(() => {
      applyThemeMode(savedThemeMode());
      return inject(AuthService).restoreSession();
    }),
  ],
};
