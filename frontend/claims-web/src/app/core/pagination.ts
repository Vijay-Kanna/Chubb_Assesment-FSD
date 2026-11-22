import { HttpResourceRef } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, WritableSignal, effect, input, linkedSignal, output } from '@angular/core';

export const pageSize = 10;

export function pageParameters(page: number) {
  return new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
}

export function pageCount(total: number) {
  return Math.min(10000, Math.max(1, Math.ceil(total / pageSize)));
}

export function trackPageTotal<T>(resource: HttpResourceRef<T>, page: WritableSignal<number>) {
  const total = linkedSignal({
    source: () => resource.headers()?.get('X-Total-Count'),
    computation: (header, previous): number | null =>
      header == null ? previous?.value ?? null : Number(header),
  });

  effect(() => {
    if (resource.isLoading() || !resource.hasValue()) return;
    const count = total();
    if (count !== null && page() > pageCount(count)) page.set(pageCount(count));
  });

  return total;
}

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (total() !== null) {
      <nav [attr.aria-label]="label()" class="pagination">
        <button type="button" class="quiet" [attr.aria-disabled]="loading() || page() <= 1"
          (click)="goTo(page() - 1)">{{ previousLabel() }}</button>
        <span role="status">Page {{ page() }} of {{ pages() }}</span>
        <button type="button" class="quiet" [attr.aria-disabled]="loading() || page() >= pages()"
          (click)="goTo(page() + 1)">{{ nextLabel() }}</button>
      </nav>
    }
  `,
  styles: `
    :host { display: block; }
    .pagination {
      display: flex; flex-wrap: wrap; align-items: center; justify-content: flex-end;
      gap: .5rem .75rem; padding: .65rem .85rem; border-top: 1px solid var(--line);
      color: var(--muted); font-size: .8rem; font-weight: 800;
    }
    .pagination button { min-height: 44px; padding-inline: .55rem; font-size: .78rem; }
    .pagination button[aria-disabled="true"] { opacity: .55; cursor: default; }
    @media (max-width: 450px) {
      .pagination { justify-content: space-between; padding-inline: .55rem; gap: .35rem; }
    }
  `,
})
export class Pagination {
  readonly total = input.required<number | null>();
  readonly page = input.required<number>();
  readonly label = input.required<string>();
  readonly loading = input(false);
  readonly previousLabel = input('Previous');
  readonly nextLabel = input('Next');
  readonly pageChange = output<number>();

  protected pages() {
    return pageCount(this.total() ?? 0);
  }

  protected goTo(page: number) {
    if (!this.loading() && page >= 1 && page <= this.pages()) this.pageChange.emit(page);
  }
}
