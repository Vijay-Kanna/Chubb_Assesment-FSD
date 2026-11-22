import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { Session, sessionInterceptor } from './core/session';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes, withComponentInputBinding()),
    provideAppInitializer(() => inject(Session).restore()),
    provideHttpClient(withInterceptors([sessionInterceptor])),
  ],
};
