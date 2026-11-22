import { HttpResourceRef } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, Signal, WritableSignal, effect, input, linkedSignal, output } from '@angular/core';

const pageSizes = [10, 25, 50, 100, 250, 500] as const;
export const defaultPageSize = pageSizes[0];

export function pageParameters(page: number, pageSize: number) {
  return new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
}

export function pageCount(total: number, pageSize: number) {
  return Math.min(10000, Math.max(1, Math.ceil(total / pageSize)));
}

export function trackPageTotal<T>(resource: HttpResourceRef<T>, page: WritableSignal<number>, size: Signal<number>) {
  const total = linkedSignal({
    source: () => resource.headers()?.get('X-Total-Count'),
    computation: (header, previous): number | null =>
      header == null ? previous?.value ?? null : Number(header),
  });

  effect(() => {
    if (resource.isLoading() || !resource.hasValue()) return;
    const count = total();
    if (count === null) return;
    const last = pageCount(count, size());
    if (page() > last) page.set(last);
  });

  return total;
}

@Component({
  selector: 'app-pagination',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (total() !== null) {
      <nav [attr.aria-label]="label()" class="pagination">
        <label class="page-size">Items per page
          <select #sizePicker (change)="selectSize(sizePicker.value)">
            @for (option of sizes; track option) {
              <option [value]="option" [selected]="option === size()">{{ option }}</option>
            }
          </select>
        </label>
        <div class="page-actions">
          <button type="button" class="quiet" [attr.aria-disabled]="loading() || page() <= 1"
            (click)="goTo(page() - 1)">{{ previousLabel() }}</button>
          <span role="status">Page {{ page() }} of {{ pages() }}</span>
          <button type="button" class="quiet" [attr.aria-disabled]="loading() || page() >= pages()"
            (click)="goTo(page() + 1)">{{ nextLabel() }}</button>
        </div>
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
    .page-size, .page-actions { display: flex; align-items: center; gap: .5rem; }
    .page-size { white-space: nowrap; }
    .page-size select { width: auto; min-height: 44px; }
    .page-actions span { white-space: nowrap; }
    .pagination button { min-height: 44px; padding-inline: .55rem; font-size: .78rem; }
    .pagination button[aria-disabled="true"] { opacity: .55; cursor: default; }
    @media (max-width: 450px) {
      .pagination { justify-content: flex-start; padding-inline: .55rem; }
      .page-actions { flex: 1 0 100%; justify-content: space-between; }
    }
  `,
})
export class Pagination {
  readonly total = input.required<number | null>();
  readonly page = input.required<number>();
  readonly label = input.required<string>();
  readonly size = input.required<number>();
  readonly loading = input(false);
  readonly previousLabel = input('Previous');
  readonly nextLabel = input('Next');
  readonly pageChange = output<number>();
  readonly sizeChange = output<number>();
  protected readonly sizes = pageSizes;

  protected pages() {
    return pageCount(this.total() ?? 0, this.size());
  }

  protected selectSize(value: string) {
    const size = this.sizes.find(option => String(option) === value);
    if (size !== undefined) this.sizeChange.emit(size);
  }

  protected goTo(page: number) {
    if (!this.loading() && page >= 1 && page <= this.pages()) this.pageChange.emit(page);
  }
}
