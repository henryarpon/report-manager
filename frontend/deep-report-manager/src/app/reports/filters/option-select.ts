import { Component, computed, inject, input, output } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectChange, MatSelectModule } from '@angular/material/select';
import { catchError, map, of, startWith, switchMap } from 'rxjs';
import { LookupItem, LookupSource } from '../report.models';
import { ReportsApi, syncErrorMessage } from '../reports-api';

interface OptionsState {
  loading: boolean;
  items: LookupItem[];
  error: string | null;
}

/**
 * Select over a fixed option list: enum options from the catalog, or a small lookup
 * (provinces, focus zones) loaded once from the API.
 */
@Component({
  selector: 'app-option-select',
  imports: [MatFormFieldModule, MatSelectModule],
  template: `
    <mat-form-field appearance="outline" class="value-field wide" subscriptSizing="dynamic">
      <mat-label>{{ label() }}</mat-label>
      <mat-select
        [multiple]="multiple()"
        [value]="multiple() ? selectedValues() : selectedValues()[0]"
        (selectionChange)="change($event)"
      >
        @for (option of state().items; track option.value) {
          <mat-option [value]="option.value">{{ option.label }}</mat-option>
        }
      </mat-select>
      @if (state().error; as error) {
        <mat-hint class="error-text">{{ error }}</mat-hint>
      } @else if (state().loading) {
        <mat-hint>Loading options…</mat-hint>
      }
    </mat-form-field>
  `,
})
export class OptionSelect {
  private readonly api = inject(ReportsApi);

  readonly label = input.required<string>();
  /** Fixed options (enums). Ignored when `source` is set. */
  readonly options = input<LookupItem[]>([]);
  readonly source = input<LookupSource | null>(null);
  readonly multiple = input(true);
  readonly items = input<LookupItem[]>([]);
  readonly itemsChange = output<LookupItem[]>();

  private readonly loaded = toSignal(
    toObservable(this.source).pipe(
      switchMap((source) =>
        source
          ? this.api.staticLookup(source).pipe(
              map((items): OptionsState => ({ loading: false, items, error: null })),
              catchError((e: unknown) => of<OptionsState>({ loading: false, items: [], error: syncErrorMessage(e) })),
              startWith<OptionsState>({ loading: true, items: [], error: null }),
            )
          : of(null),
      ),
    ),
    { initialValue: null },
  );

  protected readonly state = computed<OptionsState>(
    () => this.loaded() ?? { loading: false, items: this.options(), error: null },
  );

  protected readonly selectedValues = computed(() => this.items().map((i) => i.value));

  protected change(event: MatSelectChange): void {
    const values = new Set<unknown>(this.multiple() ? (event.value as unknown[]) : [event.value]);
    this.itemsChange.emit(this.state().items.filter((i) => values.has(i.value)));
  }
}
