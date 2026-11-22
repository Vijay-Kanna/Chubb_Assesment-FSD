import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { form, FormField, maxLength, required, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowLeft, lucideArrowRight } from '@ng-icons/lucide';
import { NewClaim, todayUtc } from '../core/claims';
import { ClaimsClient, errorMessage } from '../core/api';

@Component({
  imports: [RouterLink, FormField, NgIcon],
  viewProviders: [provideIcons({ lucideArrowLeft, lucideArrowRight })],
  templateUrl: './new-claim.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NewClaimPage {
  private readonly client = inject(ClaimsClient);
  private readonly router = inject(Router);
  private readonly draft = signal<NewClaim>({ type: 'Motor', incidentDate: '', description: '' });
  protected readonly today = todayUtc();
  protected readonly error = signal('');
  protected readonly claim = form(this.draft, fields => {
    required(fields.type);
    required(fields.incidentDate, { message: 'Enter the incident date.' });
    required(fields.description, { message: 'Describe what happened.' });
    maxLength(fields.description, 2000);
  });

  protected save(event: Event) {
    event.preventDefault();
    this.error.set('');
    void submit(this.claim, async () => {
      const draft = this.draft();
      if (draft.incidentDate > todayUtc() || !draft.description.trim()) {
        this.error.set('Enter a date on or before today and describe what happened.');
        return;
      }
      try {
        const saved = await this.client.submit({ ...draft, description: draft.description.trim() });
        await this.router.navigate(['/claims', saved.id], { replaceUrl: true });
      } catch (error) {
        this.error.set(errorMessage(error, 'Could not submit your claim. Check the details and try again.'));
      }
    });
  }
}
