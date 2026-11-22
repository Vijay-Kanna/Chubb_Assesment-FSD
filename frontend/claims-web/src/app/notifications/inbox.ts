import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideBell } from '@ng-icons/lucide';
import { Notification, claimReference, notificationsApi } from '../core/claims';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';
import { Pagination, defaultPageSize, pageParameters, trackPageTotal } from '../core/pagination';

@Component({
  imports: [DatePipe, RouterLink, NgIcon, Pagination],
  viewProviders: [provideIcons({ lucideArrowRight, lucideBell })],
  templateUrl: './inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Inbox {
  protected readonly live = inject(LiveUpdates);
  protected readonly reference = claimReference;
  protected readonly pageSize = signal<number>(defaultPageSize);
  protected readonly page = signal(1);
  protected readonly notices = httpResource<Notification[]>(() =>
    notificationsApi + '?' + pageParameters(this.page(), this.pageSize()));
  protected readonly total = trackPageTotal(this.notices, this.page, this.pageSize);

  constructor() {
    reloadOnLiveUpdate(this.notices, this.live.notificationsVersion);
  }

}
