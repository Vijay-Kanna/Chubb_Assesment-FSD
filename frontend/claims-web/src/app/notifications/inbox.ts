import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideBell } from '@ng-icons/lucide';
import { Notification, claimReference, notificationsApi } from '../core/claims';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';

@Component({
  imports: [DatePipe, RouterLink, NgIcon],
  viewProviders: [provideIcons({ lucideArrowRight, lucideBell })],
  templateUrl: './inbox.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Inbox {
  protected readonly live = inject(LiveUpdates);
  protected readonly reference = claimReference;
  protected readonly page = signal(1);
  protected readonly notices = httpResource<Notification[]>(() =>
    notificationsApi + '?page=' + this.page());

  constructor() {
    reloadOnLiveUpdate(this.notices, this.live.notificationsVersion);
  }

  protected changePage(delta: number) {
    this.page.update(value => value + delta);
  }
}
