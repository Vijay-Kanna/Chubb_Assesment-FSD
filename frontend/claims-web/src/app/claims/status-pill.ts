import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { ClaimStatus, statusLabels } from '../core/claims';

@Component({
  selector: 'app-status-pill',
  template: '<span class="status-pill" [attr.data-status]="status()">{{ labels[status()] }}</span>',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPill {
  readonly status = input.required<ClaimStatus>();
  protected readonly labels = statusLabels;
}
