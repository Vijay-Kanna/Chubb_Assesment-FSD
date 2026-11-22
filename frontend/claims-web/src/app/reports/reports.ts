import { CurrencyPipe, DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideChartNoAxesColumnIncreasing, lucideRefreshCw } from '@ng-icons/lucide';
import { ClaimStatus, claimsApi, statusLabels } from '../core/claims';

interface ReportsOverview {
  generatedAt: string;
  days: number;
  openClaims: number;
  unassigned: number;
  awaitingInformation: number;
  missingExposure: number;
  statuses: { status: ClaimStatus; count: number }[];
  exposure: { currency: string; amountMinor: number; claims: number }[];
  staff: { name: string; openClaims: number; decisions: number; settlements: number }[];
}

@Component({
  imports: [CurrencyPipe, DatePipe, NgIcon],
  viewProviders: [provideIcons({ lucideChartNoAxesColumnIncreasing, lucideRefreshCw })],
  templateUrl: './reports.html',
  styleUrl: './reports.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Reports {
  protected readonly labels = statusLabels;
  protected readonly windows = [7, 30, 90] as const;
  protected readonly days = signal(30);
  protected readonly overview = httpResource<ReportsOverview>(() =>
    claimsApi + '/reports/overview?days=' + this.days(),
  );
  protected readonly peak = computed(() => Math.max(1, ...(this.overview.hasValue()
    ? this.overview.value().statuses.map(row => row.count) : [])));
  protected readonly decisions = computed(() => this.overview.hasValue()
    ? this.overview.value().staff.reduce((total, row) => total + row.decisions, 0) : 0);

  protected selectWindow(value: string) {
    const days = this.windows.find(window => String(window) === value);
    if (days !== undefined) this.days.set(days);
  }
}
