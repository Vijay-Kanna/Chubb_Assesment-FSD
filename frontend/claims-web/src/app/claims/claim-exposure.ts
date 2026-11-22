import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, linkedSignal, signal } from '@angular/core';
import { form, FormField, required, submit } from '@angular/forms/signals';
import { ClaimCurrency, amountValidationMessage, claimCurrencies, claimUrl, parseClaimAmount } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';

import { SearchSelect } from '../core/search-select';

@Component({
  selector: 'app-claim-exposure',
  imports: [CurrencyPipe, FormField, SearchSelect],
  templateUrl: './claim-exposure.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimExposure {
  private readonly client = inject(ClaimsClient);
  private readonly live = inject(LiveUpdates);
  readonly id = input.required<string>();
  readonly editable = input.required<boolean>();
  protected readonly currencies = claimCurrencies;
  protected readonly editing = linkedSignal({ source: this.id, computation: () => false });
  protected readonly error = signal('');
  private readonly draft = signal({ amount: '', currency: 'MYR' as ClaimCurrency });
  protected readonly estimateForm = form(this.draft, fields => {
    required(fields.amount, { message: 'Enter an exposure estimate.' });
    required(fields.currency);
  });
  protected readonly estimate = httpResource<{ amountMinor: number | null; currency: ClaimCurrency | null }>(
    () => claimUrl(this.id()) + '/exposure',
  );

  constructor() {
    reloadOnLiveUpdate(this.estimate, this.live.claimsVersion);
  }

  protected edit() {
    const present = this.estimate.hasValue() ? this.estimate.value() : null;
    this.draft.set({
      amount: present?.amountMinor != null ? (present.amountMinor / 100).toFixed(2) : '',
      currency: present?.currency ?? 'MYR',
    });
    this.error.set('');
    this.editing.set(true);
  }

  protected save(event: Event) {
    event.preventDefault();
    this.error.set('');
    const id = this.id();
    void submit(this.estimateForm, async () => {
      const values = this.draft();
      const amount = parseClaimAmount(values.amount);
      if (amount === null) {
        this.error.set(amountValidationMessage);
        return;
      }
      try {
        await this.client.saveExposure(id, amount, values.currency);
        if (id === this.id()) {
          this.editing.set(false);
          this.estimate.reload();
        }
      } catch (error) {
        this.error.set(errorMessage(error, 'Could not save the exposure estimate.'));
      }
    });
  }
}
