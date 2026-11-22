import { Injectable, ResourceRef, Signal, WritableSignal, effect, inject, signal } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Session } from './session';

@Injectable({ providedIn: 'root' })
export class LiveUpdates {
  private readonly session = inject(Session);
  private readonly claimsConnection = signal(false);
  private readonly notificationsConnection = signal(false);
  private readonly claimsRevision = signal(0);
  private readonly notificationsRevision = signal(0);
  readonly claimsConnected = this.claimsConnection.asReadonly();
  readonly notificationsConnected = this.notificationsConnection.asReadonly();
  readonly claimsVersion = this.claimsRevision.asReadonly();
  readonly notificationsVersion = this.notificationsRevision.asReadonly();

  constructor() {
    effect(onCleanup => {
      const session = this.session.current();
      this.claimsConnection.set(false);
      this.notificationsConnection.set(false);
      if (!session) return;

      const disconnectClaims = this.connect('/api/claims/hubs/claims', session.accessToken,
        'ClaimsChanged', this.claimsConnection, this.claimsRevision);
      const disconnectNotifications = session.role === 'Claimant'
        ? this.connect('/api/live/notifications', session.accessToken,
          'NotificationsChanged', this.notificationsConnection, this.notificationsRevision)
        : undefined;

      onCleanup(() => {
        disconnectClaims();
        disconnectNotifications?.();
      });
    });
  }

  private connect(url: string, accessToken: string, event: string, connected: WritableSignal<boolean>,
    revision: WritableSignal<number>): () => void {
    const connection = new HubConnectionBuilder()
      .withUrl(url, { accessTokenFactory: () => accessToken })
      .withAutomaticReconnect([0, 1000, 3000, 7500])
      .configureLogging(LogLevel.None)
      .build();

    let disposed = false;
    let retry: ReturnType<typeof setTimeout> | undefined;

    const invalidate = () => {
      if (!disposed) revision.update(value => value + 1);
    };
    const ready = () => {
      if (disposed) return;
      connected.set(true);
      invalidate();
    };
    const disconnected = () => {
      if (!disposed) connected.set(false);
    };
    const retryStart = () => {
      disconnected();
      clearTimeout(retry);
      if (!disposed) retry = setTimeout(() => void start(), 2500);
    };
    const start = async () => {
      if (disposed) return;
      try {
        await connection.start();
        ready();
      } catch {
        retryStart();
      }
    };

    connection.on(event, invalidate);
    connection.onreconnecting(disconnected);
    connection.onreconnected(ready);
    connection.onclose(retryStart);
    void start();

    return () => {
      disposed = true;
      clearTimeout(retry);
      void connection.stop().catch(() => undefined);
    };
  }
}

export function reloadOnLiveUpdate<T>(resource: ResourceRef<T>, changes: Signal<number>): void {
  let applied = changes();
  effect(() => {
    const revision = changes();
    if (revision === applied || resource.isLoading()) return;

    // Keep the current view during reload and apply changes that arrived during a request.
    applied = revision;
    resource.reload();
  });
}
