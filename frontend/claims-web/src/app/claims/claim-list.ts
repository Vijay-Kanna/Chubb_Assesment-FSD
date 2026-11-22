import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowDownRight, lucideArrowRight, lucideClipboardList, lucidePlus } from '@ng-icons/lucide';
import { Claim, ClaimStatus, claimReference, claimStatuses, claimsApi, statusLabels } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { Session } from '../core/session';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';
import { StatusPill } from './status-pill';

@Component({
  imports: [DatePipe, RouterLink, NgIcon, StatusPill],
  viewProviders: [provideIcons({ lucideArrowDownRight, lucideArrowRight, lucideClipboardList, lucidePlus })],
  templateUrl: './claim-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimList {
  private readonly live = inject(LiveUpdates);
  private readonly client = inject(ClaimsClient);
  private readonly localRevision = signal(0);
  private readonly revision = computed(() => this.live.claimsVersion() + this.localRevision());
  readonly conversationList = input(false, { transform: booleanAttribute });
  protected readonly session = inject(Session);
  protected readonly isQueue = computed(() => this.session.isStaff() && !this.conversationList());
  protected readonly rowRoute = computed(() => this.conversationList() ? '/conversations' : this.session.listRoute());
  protected readonly pickupError = signal('');
  protected readonly assignedClaim = signal<Claim | null>(null);
  protected readonly taking = signal<ReadonlySet<string>>(new Set());
  readonly requestedStatus = input<string | undefined>(undefined, { alias: 'status' });
  protected readonly filter = linkedSignal<ClaimStatus | ''>(() => {
    const status = this.requestedStatus();
    if (status === 'all' || this.conversationList()) return '';
    return claimStatuses.find(option => option === status) ?? 'Submitted';
  });
  protected readonly page = linkedSignal({ source: this.filter, computation: () => 1 });
  protected readonly statuses = claimStatuses;
  protected readonly labels = statusLabels;
  protected readonly reference = claimReference;
  protected readonly claims = httpResource<Claim[]>(() => {
    const params = new URLSearchParams({ page: String(this.page()) });
    if (this.filter()) params.set('status', this.filter());
    const user = this.session.current();
    if (this.conversationList() && user?.role === 'ClaimsStaff') {
      params.set('assignedStaffId', user.userId);
    }
    return claimsApi + '/claims?' + params.toString();
  });
  protected readonly loadError = computed(() => errorMessage(this.claims.error(), 'Could not load the claims.'));
  protected readonly responseNeeded = computed(() =>
    this.claims.hasValue() && this.claims.value().some(claim => claim.status === 'AwaitingInformation'),
  );

  constructor() {
    reloadOnLiveUpdate(this.claims, this.revision);
  }

  protected changeFilter(value: string) {
    const status = this.statuses.find(option => option === value) ?? '';
    this.filter.set(status);
  }

  protected changePage(delta: number) {
    this.page.update(value => value + delta);
  }

  protected async pickUp(claim: Claim) {
    if (this.taking().has(claim.id)) return;

    this.taking.update(current => new Set(current).add(claim.id));
    this.pickupError.set('');
    this.assignedClaim.set(null);
    try {
      this.assignedClaim.set(await this.client.review(claim.id, 'pick-up'));
    } catch (error) {
      const reference = claimReference(claim.id);
      this.pickupError.set(error instanceof HttpErrorResponse && error.status === 409
        ? `${reference} has already been picked up. Choose another claim from the queue.`
        : errorMessage(error, `Could not pick up ${reference}.`));
    } finally {
      this.taking.update(current => {
        const pending = new Set(current);
        pending.delete(claim.id);
        return pending;
      });
      this.localRevision.update(value => value + 1);
    }
  }
}
