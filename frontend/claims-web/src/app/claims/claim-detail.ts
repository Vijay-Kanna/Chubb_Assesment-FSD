import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowLeft, lucideCheck, lucideClipboardList, lucideHistory,
  lucideMessageCircle, lucideRefreshCw, lucideSend,
} from '@ng-icons/lucide';
import { Claim, ClaimMessage, HistoryEntry, ReviewAction, claimReference, claimUrl, historyLabels } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { Session } from '../core/session';
import { StatusPill } from './status-pill';

const decisionLabels = { approve: 'Approve claim', reject: 'Reject claim', settle: 'Record settlement' };
type Decision = keyof typeof decisionLabels;

@Component({
  imports: [DatePipe, RouterLink, FormField, NgIcon, StatusPill],
  viewProviders: [provideIcons({
    lucideArrowLeft, lucideCheck, lucideClipboardList, lucideHistory,
    lucideMessageCircle, lucideRefreshCw, lucideSend,
  })],
  templateUrl: './claim-detail.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimDetail {
  private readonly client = inject(ClaimsClient);
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
  protected readonly loadError = computed(() => errorMessage(this.claim.error(), 'Could not load this claim.'));
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

  protected refresh() {
    this.pendingDecision.set(null);
    this.claim.reload();
    this.messages.reload();
    this.history.reload();
  }

  protected async change(action: ReviewAction, body?: string) {
    const id = this.id();
    if (!id || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.feedback.set('');
    try {
      const updated = await this.client.review(id, action, body);
      if (this.id() === id) {
        this.claim.set(updated);
        this.messages.reload();
        this.history.reload();
        this.pendingDecision.set(null);
        if (body) this.draft.set({ body: '' });
        this.feedback.set(action === 'questions' || action === 'replies' ? 'Message sent.' : 'Claim updated.');
      }
    } catch (error) {
      if (this.id() === id) {
        this.error.set(errorMessage(error, 'Could not update this claim.'));
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
}
