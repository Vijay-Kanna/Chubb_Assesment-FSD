import { CurrencyPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, linkedSignal, signal } from '@angular/core';
import { form, FormField, required, submit } from '@angular/forms/signals';
import { claimUrl } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';

const currencies = ['MYR', 'SGD', 'AUD', 'HKD', 'NZD', 'USD'] as const;
type Currency = typeof currencies[number];

@Component({
  selector: 'app-claim-exposure',
  imports: [CurrencyPipe, FormField],
  templateUrl: './claim-exposure.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClaimExposure {
  private readonly client = inject(ClaimsClient);
  private readonly live = inject(LiveUpdates);
  readonly id = input.required<string>();
  readonly editable = input.required<boolean>();
  protected readonly currencies = currencies;
  protected readonly editing = linkedSignal({ source: this.id, computation: () => false });
  protected readonly error = signal('');
  private readonly draft = signal({ amount: '', currency: 'MYR' as Currency });
  protected readonly estimateForm = form(this.draft, fields => {
    required(fields.amount, { message: 'Enter an exposure estimate.' });
    required(fields.currency);
  });
  protected readonly estimate = httpResource<{ amountMinor: number | null; currency: Currency | null }>(
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
      const amount = Number(values.amount);
      if (!/^\d+(\.\d{1,2})?$/.test(values.amount.trim()) || amount > 1_000_000_000) {
        this.error.set('Enter a nonnegative amount with up to two decimal places.');
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
