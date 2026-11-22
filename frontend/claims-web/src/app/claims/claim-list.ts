import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDownRight, lucideArrowRight, lucideClipboardList, lucidePlus, lucideRefreshCw } from '@ng-icons/lucide';
import { Claim, ClaimStatus, claimReference, claimStatuses, claimsApi, statusLabels } from '../core/claims';
import { errorMessage } from '../core/api';
import { Session } from '../core/session';
import { StatusPill } from './status-pill';

@Component({
  imports: [DatePipe, RouterLink, NgIcon, StatusPill],
  viewProviders: [provideIcons({ lucideArrowDownRight, lucideArrowRight, lucideClipboardList, lucidePlus, lucideRefreshCw })],
  templateUrl: './claim-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimList {
  protected readonly session = inject(Session);
  protected readonly filter = signal<ClaimStatus | ''>('');
  protected readonly page = signal(1);
  protected readonly statuses = claimStatuses;
  protected readonly labels = statusLabels;
  protected readonly reference = claimReference;
  protected readonly claims = httpResource<Claim[]>(() => {
    const params = new URLSearchParams({ page: String(this.page()) });
    if (this.filter()) params.set('status', this.filter());
    return claimsApi + '/claims?' + params.toString();
  });
  protected readonly loadError = computed(() => errorMessage(this.claims.error(), 'Could not load the claims.'));
  protected readonly responseNeeded = computed(() =>
    this.claims.hasValue() && this.claims.value().some(claim => claim.status === 'AwaitingInformation'),
  );

  protected changeFilter(value: string) {
    const status = this.statuses.find(option => option === value) ?? '';
    this.filter.set(status);
    this.page.set(1);
  }

  protected changePage(delta: number) {
    this.page.update(value => value + delta);
  }
}
