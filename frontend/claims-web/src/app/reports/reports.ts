import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, booleanAttribute, computed, inject, input, linkedSignal, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NgIcon, provideIcons } from '@ng-icons/core';
import { lucideArrowRight, lucideDownload } from '@ng-icons/lucide';
import { ClaimStatus, claimsApi, statusLabels, todayUtc } from '../core/claims';
import { LiveUpdates, reloadOnLiveUpdate } from '../core/live-updates';
import { Pagination, defaultPageSize } from '../core/pagination';

export interface ReportTotal {
  currency: string;
  amountMinor: number;
  claims: number;
}

export interface ReportsOverview {
  generatedAt: string;
  startDate: string | null;
  endDate: string | null;
  totalClaims: number;
  openClaims: number;
  unassigned: number;
  awaitingInformation: number;
  missingExposure: number;
  statuses: { status: ClaimStatus; count: number }[];
  requested: ReportTotal[];
  approved: ReportTotal[];
  exposure: ReportTotal[];
  staff: { name: string; openClaims: number; decisions: number; settlements: number }[];
}

function recentRange(days: number) {
  const start = new Date();
  start.setUTCDate(start.getUTCDate() - days + 1);
  return { startDate: start.toISOString().slice(0, 10), endDate: todayUtc() };
}

@Component({
  imports: [DatePipe, DecimalPipe, RouterLink, NgIcon, Pagination],
  viewProviders: [provideIcons({ lucideArrowRight, lucideDownload })],
  templateUrl: './reports.html',
  styleUrl: './reports.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Reports {
  private readonly live = inject(LiveUpdates);
  readonly dashboard = input(false, { transform: booleanAttribute });
  protected readonly labels = statusLabels;
  protected readonly windows = [7, 30, 90] as const;
  protected readonly period = signal<number | 'custom'>(30);
  protected readonly range = signal(recentRange(30));
  protected readonly start = signal(this.range().startDate);
  protected readonly end = signal(this.range().endDate);
  protected readonly rangeError = signal('');
  protected readonly exportError = signal('');
  protected readonly exporting = signal(false);
  protected readonly overview = httpResource<ReportsOverview>(() =>
    claimsApi + '/reports/overview' + (this.dashboard() ? '' : '?' + new URLSearchParams(this.range())),
  );
  protected readonly pageSize = signal<number>(defaultPageSize);
  protected readonly staffPage = linkedSignal({ source: () => [this.dashboard(), this.range()], computation: () => 1 });
  protected readonly staffRows = computed(() => {
    if (!this.overview.hasValue()) return [];
    const pageSize = this.pageSize();
    const start = (this.staffPage() - 1) * pageSize;
    return this.overview.value().staff.slice(start, start + pageSize);
  });
  protected readonly peak = computed(() => Math.max(1, ...(this.overview.hasValue()
    ? this.overview.value().statuses.map(row => row.count) : [])));
  protected readonly decisions = computed(() => this.overview.hasValue()
    ? this.overview.value().staff.reduce((total, row) => total + row.decisions, 0) : 0);

  constructor() {
    reloadOnLiveUpdate(this.overview, this.live.claimsVersion);
  }

  protected selectWindow(days: number) {
    this.period.set(days);
    this.rangeError.set('');
    this.range.set(recentRange(days));
    this.start.set(this.range().startDate);
    this.end.set(this.range().endDate);
  }

  protected applyRange(event: Event) {
    event.preventDefault();
    const startDate = this.start();
    const endDate = this.end();
    if (!startDate || !endDate || startDate > endDate) {
      this.rangeError.set('Choose a start and end date, with the end date on or after the start date.');
      return;
    }
    this.rangeError.set('');
    this.range.set({ startDate, endDate });
  }

  protected async download(report: ReportsOverview) {
    this.exporting.set(true);
    this.exportError.set('');
    try {
      const { downloadReport } = await import('./report-pdf');
      downloadReport(report);
    } catch {
      this.exportError.set('Could not export the report. Try again.');
    } finally {
      this.exporting.set(false);
    }
  }
}
