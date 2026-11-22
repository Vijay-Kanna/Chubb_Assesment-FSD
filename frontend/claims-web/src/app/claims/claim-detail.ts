import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft, lucideCheck, lucideClipboardList, lucideHistory,
  lucideMail, lucideMessageCircle, lucidePhone, lucideSend,
} from '@ng-icons/lucide';
import { Claim, ClaimMessage, HistoryEntry, ReviewAction, claimReference, claimUrl, historyLabels } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { Session } from '../core/session';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';
import { StatusPill } from './status-pill';
import { ClaimExposure } from './claim-exposure';

const decisionLabels = {
  approve: 'Approve claim',
  reject: 'Reject claim',
  settle: 'Record settlement',
  'rejection-reason': 'Add decision reason',
};
type Decision = keyof typeof decisionLabels;

@Component({
  imports: [DatePipe, RouterLink, FormField, NgIcon, StatusPill, ClaimExposure],
  viewProviders: [provideIcons({
    lucideArrowLeft, lucideCheck, lucideClipboardList, lucideHistory,
    lucideMail, lucideMessageCircle, lucidePhone, lucideSend,
  })],
  templateUrl: './claim-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimDetail {
  private readonly client = inject(ClaimsClient);
  private readonly live = inject(LiveUpdates);
  private readonly localRevision = signal(0);
  private readonly version = computed(() => this.live.claimsVersion() + this.localRevision());
  readonly id = input<string>();
  protected readonly session = inject(Session);
  protected readonly reference = claimReference;
  protected readonly historyLabels = historyLabels;
  protected readonly decisionLabels = decisionLabels;
  protected readonly pendingDecision = linkedSignal({
    source: this.id,
    computation: (): Decision | null => null,
  });
  protected readonly busy = signal(false);
  protected readonly error = linkedSignal({ source: this.id, computation: () => '' });
  protected readonly feedback = linkedSignal({ source: this.id, computation: () => '' });
  private readonly draft = linkedSignal({ source: this.id, computation: () => ({ body: '' }) });
  protected readonly messageForm = form(this.draft, fields => {
    required(fields.body, { message: 'Enter a message.' });
    maxLength(fields.body, 2000);
  });
  private readonly rejectionDraft = linkedSignal({ source: this.id, computation: () => ({ reason: '' }) });
  protected readonly rejectionForm = form(this.rejectionDraft, fields => {
    required(fields.reason, { message: 'Enter a reason for the decision.' });
    maxLength(fields.reason, 1000, { message: 'Use at most 1,000 characters.' });
  });

  protected readonly claim = httpResource<Claim>(() => {
    const id = this.id();
    return id ? claimUrl(id) : undefined;
  });
  protected readonly isAssigned = computed(() =>
    this.claim.hasValue() && this.claim.value().assignedStaffId === this.session.current()?.userId,
  );
  protected readonly canReadMessages = computed(() => !this.session.isStaff() || this.isAssigned());
  protected readonly messages = httpResource<ClaimMessage[]>(() => {
    const id = this.id();
    return id && this.claim.hasValue() && this.canReadMessages() ? claimUrl(id) + '/messages' : undefined;
  });
  protected readonly history = httpResource<HistoryEntry[]>(() => {
    const id = this.id();
    return id && this.claim.hasValue() ? claimUrl(id) + '/history' : undefined;
  });
  protected readonly canSend = computed(() => this.claim.hasValue() && (
    this.session.isStaff()
      ? this.isAssigned() && this.claim.value().status === 'UnderReview'
      : this.claim.value().status === 'AwaitingInformation'
  ));
  protected readonly noLongerAvailable = computed(() => {
    const error = this.claim.error();
    return this.session.isStaff() && error instanceof HttpErrorResponse && error.status === 404;
  });
  protected readonly loadError = computed(() => this.error() || (this.noLongerAvailable()
    ? 'This claim is no longer available to you. Another staff member may have picked it up.'
    : errorMessage(this.claim.error(), 'Could not load this claim.')));
  protected readonly stages = ['Submitted', 'In review', 'Decision', 'Settled'];
  protected readonly stage = computed(() => {
    if (!this.claim.hasValue()) return 0;
    switch (this.claim.value().status) {
      case 'Submitted': return 0;
      case 'UnderReview':
      case 'AwaitingInformation': return 1;
      case 'Approved':
      case 'Rejected': return 2;
      case 'Settled': return 3;
    }
  });

  constructor() {
    reloadOnLiveUpdate(this.claim, this.version);
    reloadOnLiveUpdate(this.messages, this.version);
    reloadOnLiveUpdate(this.history, this.version);
  }

  protected refresh() {
    this.pendingDecision.set(null);
    this.localRevision.update(value => value + 1);
  }

  protected async change(action: ReviewAction, body?: string) {
    const id = this.id();
    if (!id || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.feedback.set('');
    try {
      await this.client.review(id, action, body);
      if (this.id() === id) {
        this.localRevision.update(value => value + 1);
        this.pendingDecision.set(null);
        if (action === 'questions' || action === 'replies') this.draft.set({ body: '' });
        if (action === 'reject' || action === 'rejection-reason') this.rejectionDraft.set({ reason: '' });
        this.feedback.set(action === 'questions' || action === 'replies' ? 'Message sent.' : 'Claim updated.');
      }
    } catch (error) {
      if (this.id() === id) {
        this.error.set(action === 'pick-up' && error instanceof HttpErrorResponse && error.status === 409
          ? 'Another staff member has already picked up this claim. Return to the queue to choose another claim.'
          : errorMessage(error, 'Could not update this claim.'));
        this.refresh();
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected send(event: Event) {
    event.preventDefault();
    this.error.set('');
    void submit(this.messageForm, async () => {
      const body = this.draft().body.trim();
      if (!body) {
        this.error.set('Enter a message.');
        return;
      }
      await this.change(this.session.isStaff() ? 'questions' : 'replies', body);
    });
  }

  protected recordRejection(event: Event, action: 'reject' | 'rejection-reason') {
    event.preventDefault();
    this.error.set('');
    void submit(this.rejectionForm, async () => {
      const reason = this.rejectionDraft().reason.trim();
      if (!reason) {
        this.error.set('Enter a reason for the decision.');
        return;
      }
      await this.change(action, reason);
    });
  }
}
