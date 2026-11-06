import { Component, computed, inject, input, output, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { catchError, debounceTime, map, of, startWith, switchMap } from 'rxjs';
import { LookupItem, LookupSource } from '../report.models';
import { ReportsApi, syncErrorMessage } from '../reports-api';

type SearchState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'done'; items: LookupItem[] }
  | { status: 'error'; message: string };

/**
 * Server-side typeahead for id lookups (customers, programs, outlets).
 * Picks are listed under the input, each with its own remove button.
 */
@Component({
  selector: 'app-lookup-typeahead',
  imports: [MatAutocompleteModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <mat-form-field appearance="outline" class="value-field" subscriptSizing="dynamic">
      <mat-label>{{ label() }}</mat-label>
      <mat-icon matIconPrefix aria-hidden="true">search</mat-icon>
      <input
        #searchInput
        matInput
        [placeholder]="placeholder()"
        [matAutocomplete]="auto"
        [disabled]="disabled()"
        [value]="search()"
        (input)="search.set(searchInput.value)"
      />
      <mat-autocomplete #auto [displayWith]="clearInput" (optionSelected)="select($event)">
        @for (option of options(); track option.value) {
          <mat-option [value]="option">{{ option.label }}</mat-option>
        }
      </mat-autocomplete>
      @if (hint(); as hintText) {
        <mat-hint [class.error-text]="state().status === 'error'">{{ hintText }}</mat-hint>
      }
    </mat-form-field>
    @if (items().length) {
      <ul class="tag-list picks" [attr.aria-label]="'Selected ' + label().toLowerCase()">
        @for (item of items(); track item.value) {
          <li class="tag">
            <span class="tag-label" [title]="item.label">{{ item.label }}</span>
            <button type="button" class="tag-remove" [attr.aria-label]="'Remove ' + item.label" (click)="remove(item)">
              <mat-icon aria-hidden="true">close</mat-icon>
            </button>
          </li>
        }
      </ul>
    }
  `,
})
export class LookupTypeahead {
  private readonly api = inject(ReportsApi);

  readonly source = input.required<LookupSource>();
  readonly label = input.required<string>();
  readonly minChars = input(0);
  /** Scope for the outlet lookup. */
  readonly programIds = input<readonly number[]>([]);
  readonly disabled = input(false);
  readonly disabledHint = input('');
  readonly items = input<LookupItem[]>([]);
  readonly itemsChange = output<LookupItem[]>();

  protected readonly search = signal('');

  // Compared by content so a new but equal programIds array (any draft edit rebuilds it) does not re-fire the lookup.
  private readonly query = computed(
    () => ({
      source: this.source(),
      term: this.search().trim(),
      minChars: this.minChars(),
      programIds: this.programIds(),
      disabled: this.disabled(),
    }),
    {
      equal: (a, b) =>
        a.source === b.source &&
        a.term === b.term &&
        a.minChars === b.minChars &&
        a.disabled === b.disabled &&
        a.programIds.length === b.programIds.length &&
        a.programIds.every((id, i) => id === b.programIds[i]),
    },
  );

  protected readonly state = toSignal(
    toObservable(this.query).pipe(
      debounceTime(250),
      switchMap((q) => {
        if (q.disabled || q.term.length < q.minChars) {
          return of<SearchState>({ status: 'idle' });
        }
        return this.api.lookup(q.source, q.term, q.programIds).pipe(
          map((items): SearchState => ({ status: 'done', items })),
          catchError((e: unknown) => of<SearchState>({ status: 'error', message: syncErrorMessage(e) })),
          startWith<SearchState>({ status: 'loading' }),
        );
      }),
    ),
    { initialValue: { status: 'idle' } as SearchState },
  );

  protected readonly options = computed(() => {
    const state = this.state();
    if (state.status !== 'done') {
      return [];
    }
    const picked = new Set(this.items().map((i) => i.value));
    return state.items.filter((i) => !picked.has(i.value));
  });

  protected readonly placeholder = computed(() =>
    this.minChars() > 1 ? `Type ${this.minChars()} or more characters` : 'Type to search',
  );

  protected readonly hint = computed(() => {
    if (this.disabled()) {
      return this.disabledHint();
    }
    const state = this.state();
    const term = this.search().trim();
    switch (state.status) {
      case 'loading':
        return 'Searching…';
      case 'error':
        return state.message;
      case 'done':
        if (!state.items.length) {
          return term ? `Nothing matches “${term}”` : 'Nothing to choose from';
        }
        return term ? '' : `${state.items.length} to choose from`;
      default:
        return term && term.length < this.minChars() ? `Keep typing: ${this.minChars()} characters needed` : '';
    }
  });

  /** Picks show in the list below, so the input empties after each choice. */
  protected readonly clearInput = () => '';

  protected select(event: MatAutocompleteSelectedEvent): void {
    const item = event.option.value as LookupItem;
    this.search.set('');
    if (!this.items().some((i) => i.value === item.value)) {
      this.itemsChange.emit([...this.items(), item]);
    }
  }

  protected remove(item: LookupItem): void {
    this.itemsChange.emit(this.items().filter((i) => i.value !== item.value));
  }
}
