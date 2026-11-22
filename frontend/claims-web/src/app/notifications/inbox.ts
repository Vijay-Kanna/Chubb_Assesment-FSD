import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideBell, lucideRefreshCw } from '@ng-icons/lucide';
import { Notification, claimReference, notificationsApi } from '../core/claims';

@Component({
  imports: [DatePipe, RouterLink, NgIcon],
  viewProviders: [provideIcons({ lucideArrowRight, lucideBell, lucideRefreshCw })],
  templateUrl: './inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Inbox {
  protected readonly reference = claimReference;
  protected readonly page = signal(1);
  protected readonly notices = httpResource<Notification[]>(() => notificationsApi + '?page=' + this.page());

  constructor() {
    const interval = setInterval(() => {
      if (this.page() === 1 && !document.hidden) this.notices.reload();
    }, 10000);
    inject(DestroyRef).onDestroy(() => clearInterval(interval));
  }

  protected changePage(delta: number) {
    this.page.update(value => value + delta);
  }
}
