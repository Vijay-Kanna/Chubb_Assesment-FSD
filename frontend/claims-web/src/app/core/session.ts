import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { DestroyRef, Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, firstValueFrom, throwError } from 'rxjs';
import { Role, claimsApi, notificationsApi } from './claims';

interface SessionData {
  userId: string;
  email: string;
  expiresAt: string;
  role: Role;
}

const sessionUrl = claimsApi + '/auth/session';
export const workspaceHeaders = { 'X-Claims-Workspace': '1' };

@Injectable({ providedIn: 'root' })
export class Session {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly active = signal<SessionData | null>(null);
  private readonly channel = new BroadcastChannel('claims-workspace-session');
  private expirationTimer?: ReturnType<typeof setTimeout>;
  readonly current = this.active.asReadonly();
  readonly isStaff = computed(() => this.current()?.role === 'ClaimsStaff');
  readonly listRoute = computed(() => this.isStaff() ? '/staff' : '/claims');
  readonly signingOut = signal(false);
  readonly signOutError = signal('');

  constructor() {
    this.channel.onmessage = () => location.reload();
    inject(DestroyRef).onDestroy(() => {
      clearTimeout(this.expirationTimer);
      this.channel.close();
    });
  }

  async restore() {
    try {
      this.setSession(await firstValueFrom(this.http.get<SessionData>(sessionUrl, { timeout: 5000 })));
    } catch {
      this.active.set(null);
    }
  }

  async signIn(email: string, password: string) {
    const response = await firstValueFrom(this.http.post<SessionData>(sessionUrl, { email, password }));
    this.setSession(response);
    this.channel.postMessage('changed');
    await this.router.navigateByUrl(this.listRoute(), { replaceUrl: true });
  }

  async signOut() {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    this.signOutError.set('');
    try {
      await firstValueFrom(this.http.delete(sessionUrl, { timeout: 5000 }));
      this.channel.postMessage('changed');
      this.endSession();
    } catch {
      this.signOutError.set('Could not sign out. Please try again.');
    } finally {
      this.signingOut.set(false);
    }
  }

  endSession() {
    clearTimeout(this.expirationTimer);
    this.active.set(null);
    void this.router.navigateByUrl('/login', { replaceUrl: true });
  }

  private setSession(session: SessionData) {
    const timeRemaining = Date.parse(session.expiresAt) - Date.now();
    if (!session.userId || !Number.isFinite(timeRemaining) || timeRemaining <= 0 ||
        (session.role !== 'Claimant' && session.role !== 'ClaimsStaff')) {
      throw new Error('Invalid session response');
    }
    clearTimeout(this.expirationTimer);
    this.active.set(session);
    this.expirationTimer = setTimeout(() => this.endSession(), timeRemaining);
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

export const signedInGuard: CanActivateFn = () =>
  !!inject(Session).current() || inject(Router).parseUrl('/login');

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const isClaimsRequest = request.url.startsWith(claimsApi + '/');
  const isNotificationsRequest = request.url === notificationsApi || request.url.startsWith(notificationsApi + '?');
  if (!isClaimsRequest && !isNotificationsRequest) return next(request);

  const workspaceRequest = ['GET', 'HEAD'].includes(request.method)
    ? request : request.clone({ setHeaders: workspaceHeaders });
  if (request.url === sessionUrl || request.url === claimsApi + '/auth/login') return next(workspaceRequest);

  const session = inject(Session);
  const account = session.current();
  return next(workspaceRequest).pipe(
    catchError((error: unknown) => {
      if (isClaimsRequest && error instanceof HttpErrorResponse && error.status === 401 &&
          account && session.current() === account) {
        session.endSession();
      }
      return throwError(() => error);
    }),
  );
};
