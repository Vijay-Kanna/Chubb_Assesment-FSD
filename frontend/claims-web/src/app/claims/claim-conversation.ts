import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideArrowRight, lucideMessageCircle, lucideSend } from '@ng-icons/lucide';
import { Claim, ClaimMessage, claimReference, claimUrl } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';
import { Session } from '../core/session';
import { StatusPill } from './status-pill';
import { Pagination, defaultPageSize, pageParameters, trackPageTotal } from '../core/pagination';

@Component({
  imports: [DatePipe, FormField, NgIcon, RouterLink, StatusPill, Pagination],
  viewProviders: [provideIcons({ lucideArrowLeft, lucideArrowRight, lucideMessageCircle, lucideSend })],
  templateUrl: './claim-conversation.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimConversation {
  private readonly client = inject(ClaimsClient);
  private readonly live = inject(LiveUpdates);
  private readonly localRevision = signal(0);
  private readonly version = computed(() => this.live.claimsVersion() + this.localRevision());
  readonly id = input<string>();
  protected readonly session = inject(Session);
  protected readonly reference = claimReference;
  protected readonly error = linkedSignal({ source: this.id, computation: () => '' });
  private readonly draft = linkedSignal({ source: this.id, computation: () => ({ body: '' }) });
  protected readonly messageForm = form(this.draft, fields => {
    required(fields.body, { message: 'Enter a message.' });
    maxLength(fields.body, 2000);
  });

  protected readonly claim = httpResource<Claim>(() => {
    const id = this.id();
    return id ? claimUrl(id) : undefined;
  });
  protected readonly participant = computed(() => this.claim.hasValue() &&
    (!this.session.isStaff() || this.claim.value().assignedStaffId === this.session.current()?.userId),
  );
  protected readonly pageSize = signal<number>(defaultPageSize);
  protected readonly messagePage = linkedSignal({ source: this.id, computation: () => 1 });
  private readonly lastMessageId = linkedSignal({ source: this.id, computation: (): number | undefined => undefined });
  protected readonly messages = httpResource<ClaimMessage[]>(() => {
    const id = this.id();
    if (!id || !this.participant()) return undefined;

    const page = this.messagePage();
    const query = pageParameters(page, this.pageSize());
    if (page > 1 && this.lastMessageId() !== undefined) query.set('throughMessageId', String(this.lastMessageId()));
    return claimUrl(id) + '/messages?' + query;
  });
  protected readonly totalMessages = trackPageTotal(this.messages, this.messagePage, this.pageSize);
  protected readonly canSend = computed(() => this.claim.hasValue() && this.participant() &&
    this.claim.value().status === (this.session.isStaff() ? 'UnderReview' : 'AwaitingInformation'),
  );
  protected readonly loadError = computed(() => errorMessage(this.claim.error(), 'Could not load this conversation.'));

  constructor() {
    reloadOnLiveUpdate(this.claim, this.version);
    reloadOnLiveUpdate(this.messages, this.version);
  }

  protected openMessagePage(page: number) {
    if (this.messagePage() === 1 && this.messages.hasValue()) {
      this.lastMessageId.set(this.messages.value().at(-1)?.id);
    }
    this.messagePage.set(page);
  }

  protected send(event: Event) {
    event.preventDefault();
    this.error.set('');
    const id = this.id();
    if (!id || !this.canSend()) return;

    void submit(this.messageForm, async () => {
      const body = this.draft().body.trim();
      if (!body) {
        this.error.set('Enter a message.');
        return;
      }

      try {
        await this.client.review(id, this.session.isStaff() ? 'questions' : 'replies', body);
        if (this.id() === id) {
          this.draft.set({ body: '' });
          this.messagePage.set(1);
        }
      } catch (error) {
        if (this.id() === id) this.error.set(errorMessage(error, 'Could not send the message.'));
      } finally {
        if (this.id() === id) this.localRevision.update(value => value + 1);
      }
    });
  }
}
