import { Component, computed, ElementRef, inject, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { FilterDraft, OPERATOR_LABELS } from '../filter-draft';
import { CatalogFilter, LookupItem } from '../report.models';
import { LookupTypeahead } from './lookup-typeahead';
import { OptionSelect } from './option-select';

/** Lookups searched on the server as you type (the rest are short fixed lists). */
const TYPEAHEAD_MIN_CHARS: Partial<Record<string, number>> = { customers: 3, programs: 1, outlets: 0 };

/** One filter as a card: entity, field, a condition toggle and a value editor that depends on the field type. */
@Component({
  selector: 'app-filter-row',
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    LookupTypeahead,
    OptionSelect,
  ],
  templateUrl: './filter-row.html',
  host: {
    class: 'filter-row',
    role: 'group',
    '[class.incomplete]': 'incomplete()',
    '[attr.aria-label]': '"Filter: " + filter().label',
  },
})
export class FilterRow {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly draft = input.required<FilterDraft>();
  readonly filter = input.required<CatalogFilter>();
  /** How this filter relates to the report root; null for filters on the root's own entity. */
  readonly scope = input<string | null>(null);
  readonly programIds = input<readonly number[]>([]);
  readonly incomplete = input(false);

  readonly draftChange = output<FilterDraft>();
  readonly removed = output<void>();

  protected readonly operatorLabels = OPERATOR_LABELS;

  protected readonly editor = computed(() => {
    const filter = this.filter();
    const op = this.draft().op;
    if (filter.type === 'date') {
      return op === 'between' ? 'date-range' : 'date';
    }
    if (filter.type === 'enum') {
      return 'enum';
    }
    if (filter.lookup) {
      return filter.lookup in TYPEAHEAD_MIN_CHARS ? 'typeahead' : 'static-lookup';
    }
    return 'text';
  });

  protected readonly minChars = computed(() => TYPEAHEAD_MIN_CHARS[this.filter().lookup ?? ''] ?? 0);

  protected readonly enumOptions = computed<LookupItem[]>(() =>
    (this.filter().options ?? []).map((o) => ({ value: o.value, label: o.label, parentId: null })),
  );

  /** Cascading lookups (outlets) stay disabled until their parent filter has a value. */
  protected readonly waitingForParent = computed(() => !!this.filter().dependsOn && this.programIds().length === 0);

  /** Moves focus to the value editor (or the first control) and scrolls the card into view. */
  focus(): void {
    const root = this.host.nativeElement;
    const control =
      root.querySelector<HTMLElement>('.filter-controls input:not([disabled]), .filter-controls [role="combobox"]') ??
      root.querySelector<HTMLElement>('.filter-controls button');
    root.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    control?.focus({ preventScroll: true });
  }

  protected patch(change: Partial<FilterDraft>): void {
    this.draftChange.emit({ ...this.draft(), ...change });
  }
}
