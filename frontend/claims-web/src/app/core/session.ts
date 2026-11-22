import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import { Role, claimsApi, notificationsApi } from './claims';

interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  role: Role;
}

type SessionData = LoginResponse & { userId: string; email: string };

@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly active = signal<SessionData | null>(null);
  private expirationTimer?: ReturnType<typeof setTimeout>;
  readonly current = this.active.asReadonly();
  readonly isStaff = computed(() => this.current()?.role === 'ClaimsStaff');
  readonly listRoute = computed(() => this.isStaff() ? '/staff' : '/claims');

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.expirationTimer));
  }

  async signIn(email: string, password: string) {
    const response = await firstValueFrom(this.http.post<LoginResponse>(
      claimsApi + '/auth/login', { email, password },
    ));
    const segment = response.accessToken.split('.')[1];
    const claims: { sub?: string } = JSON.parse(atob(segment.replace(/-/g, '+').replace(/_/g, '/')));
    const timeRemaining = Date.parse(response.expiresAt) - Date.now();
    if (!claims.sub || !Number.isFinite(timeRemaining) || timeRemaining <= 0 ||
        (response.role !== 'Claimant' && response.role !== 'ClaimsStaff')) {
      throw new Error('Invalid session response');
    }
    clearTimeout(this.expirationTimer);
    this.active.set({ ...response, userId: claims.sub, email: email.trim() });
    this.expirationTimer = setTimeout(() => this.signOut(), timeRemaining);
    await this.router.navigateByUrl(this.listRoute(), { replaceUrl: true });
  }

  signOut() {
    clearTimeout(this.expirationTimer);
    this.active.set(null);
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }
}

export const roleGuard = (role: Role): CanActivateFn => () => {
  const session = inject(Session);
  const router = inject(Router);
  const current = session.current();
  if (!current) return router.parseUrl('/login');
  return current.role === role || router.parseUrl(session.listRoute());
};

export const guestGuard: CanActivateFn = () => {
  const session = inject(Session);
  return !session.current() || inject(Router).parseUrl(session.listRoute());
};

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const isClaimsRequest = request.url.startsWith(claimsApi + '/') && request.url !== claimsApi + '/auth/login';
  const isNotificationsRequest = request.url === notificationsApi || request.url.startsWith(notificationsApi + '?');
  if (!isClaimsRequest && !isNotificationsRequest) return next(request);

  const session = inject(Session);
  const token = session.current()?.accessToken;
  if (!token) return next(request);

  return next(request.clone({ setHeaders: { Authorization: 'Bearer ' + token } })).pipe(
    catchError((error: unknown) => {
      if (isClaimsRequest && error instanceof HttpErrorResponse && error.status === 401 &&
          session.current()?.accessToken === token) {
        session.signOut();
      }
      return throwError(() => error);
    }),
  );
};
