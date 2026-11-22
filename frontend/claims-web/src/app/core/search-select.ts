import { ChangeDetectionStrategy, Component, computed, input, model, signal } from '@angular/core';
import { FormValueControl } from '@angular/forms/signals';

@Component({
  selector: 'app-search-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="search-select">
      <label [for]="controlId()">{{ label() }}</label>
      <input #search type="text" role="combobox" autocomplete="off" aria-autocomplete="list"
        [id]="controlId()" [value]="open() ? query() : value()" [placeholder]="value() || 'Search options'"
        [disabled]="disabled()" [attr.aria-expanded]="open()"
        [attr.aria-controls]="controlId() + '-options'"
        [attr.aria-activedescendant]="open() && matches()[active()] ? optionId(active()) : null"
        (focus)="show()" (click)="show()" (blur)="close()" (input)="filter(search.value)"
        (keydown)="onKeydown($event)" />
      <ul role="listbox" [id]="controlId() + '-options'" [attr.aria-label]="label()" [hidden]="!open()">
        @for (option of matches(); track option; let i = $index) {
          <li role="option" [id]="optionId(i)" [attr.aria-selected]="option === value()"
            [class.highlighted]="active() === i" (mousedown)="$event.preventDefault()" (click)="choose(option)">
            {{ option }}
          </li>
        } @empty {
          <li class="no-options" role="presentation">No matching options</li>
        }
      </ul>
    </div>
  `,
  styles: `
    :host { display: block; min-width: 0; }
    .search-select { position: relative; display: grid; gap: .34rem; }
    label { color: var(--ink); font-weight: 800; font-size: .85rem; }
    input { padding-right: 1.7rem; }
    .search-select::after {
      content: ''; position: absolute; top: 2.45rem; right: .8rem; width: .42rem; height: .42rem;
      border: solid var(--muted); border-width: 0 2px 2px 0; transform: rotate(45deg); pointer-events: none;
    }
    ul {
      position: absolute; z-index: 5; top: 100%; inset-inline: 0; margin: .2rem 0 0; padding: .25rem;
      list-style: none; border: 1px solid var(--line); border-radius: 9px; background: var(--surface);
      box-shadow: 0 7px 24px #252b521f;
    }
    li { padding: .6rem; border-radius: 6px; font-size: .86rem; cursor: pointer; }
    li.highlighted, li:hover { background: var(--tint); color: var(--action); }
    li.no-options { color: var(--muted); cursor: default; }
  `,
})
export class SearchSelect implements FormValueControl<string> {
  readonly controlId = input.required<string>();
  readonly label = input.required<string>();
  readonly options = input.required<readonly string[]>();
  readonly value = model('');
  readonly touched = model(false);
  readonly disabled = input(false);
  protected readonly open = signal(false);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly matches = computed(() => {
    const query = this.query().trim().toLocaleLowerCase();
    return this.options().filter(option => option.toLocaleLowerCase().includes(query));
  });

  protected optionId(index: number) {
    return this.controlId() + '-option-' + index;
  }

  protected show() {
    if (this.open()) return;
    this.query.set('');
    this.active.set(Math.max(0, this.options().indexOf(this.value())));
    this.open.set(true);
  }

  protected close() {
    this.open.set(false);
    this.touched.set(true);
  }

  protected filter(query: string) {
    this.show();
    this.query.set(query);
    this.active.set(0);
  }

  protected choose(value: string) {
    this.value.set(value);
    this.close();
  }

  protected onKeydown(event: KeyboardEvent) {
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      if (!this.open()) {
        this.show();
        return;
      }
      const step = event.key === 'ArrowDown' ? 1 : -1;
      this.active.update(index => Math.max(0, Math.min(this.matches().length - 1, index + step)));
    }
    if (event.key === 'Escape' && this.open()) {
      event.preventDefault();
      this.close();
    }
    if (event.key === 'Enter' && this.open()) {
      event.preventDefault();
      const choice = this.matches()[this.active()];
      if (choice) this.choose(choice);
    }
  }
}
