import { LiveAnnouncer } from '@angular/cdk/a11y';
import { httpResource } from '@angular/common/http';
import {
  afterNextRender,
  Component,
  computed,
  DOCUMENT,
  effect,
  inject,
  Injector,
  linkedSignal,
  signal,
  untracked,
  viewChild,
  viewChildren,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorIntl, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { firstValueFrom } from 'rxjs';
import { countNoun, describeFilter } from './describe-report';
import { fileNameFromDisposition, saveBlob } from './download';
import { FilterDraft, newDraft, pruneDependents, selectedProgramIds, toRequest } from './filter-draft';
import { FilterRow } from './filters/filter-row';
import { ColumnPicker } from './pickers/column-picker';
import { FilterGroup, FilterPicker } from './pickers/filter-picker';
import {
  Catalog,
  CatalogFilter,
  CatalogRoot,
  ReportDefinition,
  ReportPage,
  ReportSort,
  RootKey,
  RunReportRequest,
} from './report.models';
import { ReportPaginatorIntl } from './report-paginator-intl';
import { ReportTable } from './report-table';
import { errorMessage, ReportsApi, syncErrorMessage } from './reports-api';

const PAGE_SIZES = [10, 25, 50, 100];

/** What one row means, for the report type cards. */
const ROOT_DETAILS: Record<RootKey, { icon: string; description: string }> = {
  transaction: { icon: 'receipt_long', description: 'Each visit or purchase, with its customer, program and outlet' },
  customer: { icon: 'person', description: 'Each customer, with their transaction totals' },
  program: { icon: 'campaign', description: 'Each program, with transaction, customer and outlet totals' },
};

interface Clause {
  draftId: number;
  text: string;
}

interface Notice {
  kind: 'error' | 'success';
  text: string;
}

function defaultColumns(root: CatalogRoot | undefined): string[] {
  return root?.columns.filter((c) => c.default).map((c) => c.key) ?? [];
}

/** Catalog entity name for a root key ("transaction" → "Transaction"). */
function rootEntity(key: RootKey): string {
  return key.charAt(0).toUpperCase() + key.slice(1);
}

/** Root, columns and filters only: what decides which rows a report returns. */
function definitionKey(definition: ReportDefinition | null): string {
  return definition
    ? JSON.stringify({ root: definition.root, columns: definition.columns, filters: definition.filters })
    : '';
}

/**
 * The `/reports` page. The editor (root, columns, filters) is a draft; "Run report" applies it.
 * Paging, sorting and export always use the applied definition, so the table and the file match.
 */
@Component({
  selector: 'app-reports-page',
  imports: [MatButtonModule, MatIconModule, MatProgressBarModule, ColumnPicker, FilterPicker, FilterRow, ReportTable],
  providers: [provideNativeDateAdapter(), { provide: MatPaginatorIntl, useClass: ReportPaginatorIntl }],
  templateUrl: './reports-page.html',
  styleUrl: './reports-page.scss',
})
export class ReportsPage {
  private readonly api = inject(ReportsApi);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  private readonly filterRows = viewChildren(FilterRow);
  private readonly filterPicker = viewChild(FilterPicker);

  protected readonly rootDetails = ROOT_DETAILS;
  /** The builder rail can be hidden to give the table the full width. */
  protected readonly railOpen = signal(true);

  // ---------- Catalog ----------

  protected readonly catalog = httpResource<Catalog>(() => this.api.catalogUrl);
  protected readonly catalogValue = computed(() => (this.catalog.hasValue() ? this.catalog.value() : undefined));
  protected readonly catalogError = computed(() =>
    this.catalog.status() === 'error' ? syncErrorMessage(this.catalog.error()) : null,
  );

  private readonly filterByKey = computed(
    () => new Map((this.catalogValue()?.filters ?? []).map((f) => [f.key, f] as const)),
  );

  protected readonly filterGroups = computed<FilterGroup[]>(() => {
    const groups = new Map<string, CatalogFilter[]>();
    for (const filter of this.catalogValue()?.filters ?? []) {
      groups.set(filter.entity, [...(groups.get(filter.entity) ?? []), filter]);
    }
    return [...groups].map(([entity, filters]) => ({ entity, filters }));
  });

  protected readonly pageSizeOptions = computed(() =>
    PAGE_SIZES.filter((n) => n <= (this.catalogValue()?.maxPageSize ?? 100)),
  );

  // ---------- Draft (editor) ----------

  protected readonly rootKey = signal<RootKey>('transaction');
  protected readonly root = computed(() => this.catalogValue()?.roots.find((r) => r.key === this.rootKey()));
  /** Resets to the root's defaults whenever the root changes. */
  protected readonly columns = linkedSignal(() => defaultColumns(this.root()));
  protected readonly drafts = signal<FilterDraft[]>([]);
  protected readonly showIncomplete = signal(false);

  protected readonly filterViews = computed(() => {
    const root = this.root();
    const drafts = this.drafts();
    const byKey = this.filterByKey();
    return drafts.flatMap((draft) => {
      const filter = byKey.get(draft.key);
      if (!filter || !root) {
        return [];
      }
      const ownEntity = filter.entity === rootEntity(root.key);
      return [
        {
          draft,
          filter,
          request: toRequest(draft, filter),
          // Only filters on other entities explain how they relate ("Has a transaction matching…").
          scope: ownEntity ? null : (root.filterScopes[filter.entity] ?? null),
          programIds: filter.dependsOn ? selectedProgramIds(drafts, filter.dependsOn) : [],
        },
      ];
    });
  });

  /**
   * Customer/Program reports: filters on other entities must all match the same transaction,
   * and the totals count only those transactions. Named for the entities in use.
   */
  protected readonly sameTransactionNote = computed(() => {
    const root = this.root();
    if (!root || root.key === 'transaction') {
      return null;
    }
    const own = rootEntity(root.key);
    const entities = [...new Set(this.filterViews().map((v) => v.filter.entity).filter((e) => e !== own))];
    if (!entities.length) {
      return null;
    }
    const names = entities.map((e) => e.toLowerCase()).join(' and ');
    return `The ${names} filters must match the same transaction, and the transaction totals count only those transactions.`;
  });

  /** Relationship hints for the filter picker, for entities other than the root's own. */
  protected readonly pickerScopes = computed<Record<string, string>>(() => {
    const root = this.root();
    if (!root) {
      return {};
    }
    const own = rootEntity(root.key);
    return Object.fromEntries(Object.entries(root.filterScopes).filter(([entity]) => entity !== own));
  });

  protected readonly incompleteCount = computed(() => this.filterViews().filter((v) => !v.request).length);

  private readonly draftDefinition = computed<ReportDefinition | null>(() => {
    const root = this.root();
    if (!root || this.incompleteCount() > 0) {
      return null;
    }
    return {
      root: root.key,
      columns: this.columns(),
      filters: this.filterViews().map((v) => v.request!),
      sort: root.defaultSort,
    };
  });

  // ---------- Applied report ----------

  protected readonly applied = signal<ReportDefinition | null>(null);
  /** Plain-English filter clauses of the applied report, captured when it was run. */
  protected readonly appliedClauses = signal<Clause[]>([]);
  /** The editor state behind the applied report, so unapplied edits can be discarded. */
  private readonly appliedDraft = signal<{ rootKey: RootKey; columns: string[]; drafts: FilterDraft[] } | null>(null);
  protected readonly appliedRoot = computed(() =>
    this.catalogValue()?.roots.find((r) => r.key === this.applied()?.root),
  );
  protected readonly sort = linkedSignal<ReportDefinition | null, ReportSort | null>({
    source: this.applied,
    computation: (applied) => applied?.sort ?? null,
  });
  protected readonly pageIndex = signal(0);
  protected readonly pageSize = linkedSignal(() => this.catalogValue()?.defaultPageSize ?? 25);

  protected readonly sortableKeys = computed<ReadonlySet<string>>(
    () => new Set((this.appliedRoot()?.columns ?? []).filter((c) => c.sortable).map((c) => c.key)),
  );

  /** The editor differs from what the table shows. */
  protected readonly dirty = computed(() => {
    const applied = this.applied();
    return !!applied && definitionKey(this.draftDefinition()) !== definitionKey(applied);
  });

  private readonly runRequest = computed<RunReportRequest | undefined>(() => {
    const applied = this.applied();
    const sort = this.sort();
    return applied && sort ? { ...applied, sort, page: this.pageIndex() + 1, pageSize: this.pageSize() } : undefined;
  });

  protected readonly run = httpResource<ReportPage>(() => {
    const body = this.runRequest();
    return body ? { url: this.api.runUrl, method: 'POST', body } : undefined;
  });

  /** Keeps the previous page on screen while the next one loads; cleared on error. */
  protected readonly result = linkedSignal<{ loading: boolean; value: ReportPage | undefined }, ReportPage | undefined>({
    source: () => ({ loading: this.run.isLoading(), value: this.run.hasValue() ? this.run.value() : undefined }),
    computation: (source, previous) => source.value ?? (source.loading ? previous?.value : undefined),
  });

  /** "1,501 transactions": the big number at the top of the results. */
  protected readonly countParts = computed(() => {
    const page = this.result();
    const root = this.appliedRoot();
    if (!page || !root) {
      return null;
    }
    const [number, ...noun] = countNoun(page.totalCount, root.label).split(' ');
    return { number, noun: noun.join(' '), total: page.totalCount };
  });

  /** Singular noun of the applied root: "transaction". */
  protected readonly rootNoun = computed(() => countNoun(1, this.appliedRoot()?.label ?? 'rows').split(' ')[1]);

  /** What the table shows, as one sentence, for the table caption and screen readers. */
  protected readonly headline = computed(() => {
    const parts = this.countParts();
    if (!parts) {
      return null;
    }
    const clauses = this.appliedClauses().map((c) => c.text);
    const count = `${parts.number} ${parts.noun}`;
    return clauses.length ? `${count} where ${clauses.join(' and ')}` : `All ${count}`;
  });

  protected readonly runError = computed(() =>
    this.run.status() === 'error' ? syncErrorMessage(this.run.error()) : null,
  );

  // ---------- Export ----------

  protected readonly exporting = signal(false);
  protected readonly exportNotice = signal<Notice | null>(null);

  constructor() {
    // Show the root's default report as soon as the catalog arrives, so the page opens on data.
    effect(() => {
      if (this.catalogValue() && untracked(this.applied) === null) {
        untracked(() => this.runReport());
      }
    });

    effect(() => {
      const page = this.run.hasValue() ? this.run.value() : undefined;
      if (page && !this.run.isLoading()) {
        void this.announcer.announce(`Report loaded: ${page.totalCount.toLocaleString()} rows, page ${page.page}.`);
      }
    });
  }

  // ---------- Editor actions ----------

  protected setRoot(key: RootKey): void {
    this.rootKey.set(key);
  }

  protected setColumns(keys: string[]): void {
    // Keep the catalog order so the table layout is predictable.
    const order = this.root()?.columns.map((c) => c.key) ?? [];
    this.columns.set(order.filter((k) => keys.includes(k)));
  }

  protected toggleRail(): void {
    this.railOpen.update((open) => !open);
  }

  /** Opens the builder and focuses the filter behind a clause of the report sentence. */
  protected editClause(draftId: number): void {
    this.railOpen.set(true);
    afterNextRender(
      () => {
        const row = this.filterRows().find((r) => r.draft().id === draftId);
        if (row) {
          row.focus();
        } else {
          this.filterPicker()?.focus();
        }
      },
      { injector: this.injector },
    );
  }

  /** Puts the editor back to the report on screen. */
  protected discardChanges(): void {
    const snapshot = this.appliedDraft();
    if (!snapshot) {
      return;
    }
    this.rootKey.set(snapshot.rootKey);
    this.columns(); // let the linked signal see the root change before overriding it
    this.columns.set(snapshot.columns);
    this.drafts.set(snapshot.drafts);
    this.showIncomplete.set(false);
  }

  protected addFilter(filter: CatalogFilter): void {
    this.drafts.update((drafts) => [...drafts, newDraft(filter)]);
    afterNextRender(() => this.filterRows().at(-1)?.focus(), { injector: this.injector });
  }

  protected updateFilter(draft: FilterDraft): void {
    const filters = this.catalogValue()?.filters ?? [];
    this.drafts.update((drafts) => pruneDependents(drafts.map((d) => (d.id === draft.id ? draft : d)), filters));
  }

  protected removeFilter(id: number): void {
    const filters = this.catalogValue()?.filters ?? [];
    this.drafts.update((drafts) => pruneDependents(drafts.filter((d) => d.id !== id), filters));
    this.filterPicker()?.focus();
  }

  protected clearFilters(): void {
    this.drafts.set([]);
    this.showIncomplete.set(false);
  }

  // ---------- Report actions ----------

  protected runReport(): void {
    const definition = this.draftDefinition();
    if (!definition) {
      this.showIncomplete.set(true);
      void this.announcer.announce(`Complete or remove ${this.incompleteCount()} filter(s) before running.`, 'assertive');
      return;
    }
    this.showIncomplete.set(false);
    this.exportNotice.set(null);
    // Keep a sort the user picked while the root stays the same and its column is still shown.
    const previous = this.applied();
    const previousSort = this.sort();
    // No columns selected means the root's default set (as on the server).
    const shown = definition.columns.length > 0 ? definition.columns : defaultColumns(this.root());
    const sort =
      previous?.root === definition.root && previousSort && shown.includes(previousSort.column)
        ? previousSort
        : definition.sort;
    this.pageIndex.set(0);
    this.appliedClauses.set(
      this.filterViews().map((v) => ({ draftId: v.draft.id, text: describeFilter(v.draft, v.filter) })),
    );
    this.appliedDraft.set({ rootKey: this.rootKey(), columns: this.columns(), drafts: this.drafts() });
    this.applied.set({ ...definition, sort });
  }

  protected changeSort(sort: ReportSort): void {
    this.sort.set(sort);
    this.pageIndex.set(0);
  }

  protected changePage(event: PageEvent): void {
    this.pageSize.set(event.pageSize);
    this.pageIndex.set(event.pageIndex);
  }

  protected async exportReport(): Promise<void> {
    const applied = this.applied();
    const sort = this.sort();
    if (!applied || !sort || this.exporting()) {
      return;
    }
    this.exporting.set(true);
    this.exportNotice.set(null);
    void this.announcer.announce('Preparing export…');
    try {
      const response = await firstValueFrom(this.api.export({ ...applied, sort }));
      const fileName = fileNameFromDisposition(response.headers.get('Content-Disposition')) ?? 'report.csv';
      if (!response.body) {
        throw new Error('Empty export response.');
      }
      saveBlob(this.document, response.body, fileName);
      const rowHeader = response.headers.get('X-Report-Row-Count');
      const rows = Number(rowHeader);
      const count = rowHeader !== null && Number.isFinite(rows) ? ` (${rows.toLocaleString()} rows)` : '';
      this.exportNotice.set({ kind: 'success', text: `Downloaded ${fileName}${count}.` });
    } catch (error) {
      this.exportNotice.set({ kind: 'error', text: await errorMessage(error) });
    } finally {
      this.exporting.set(false);
    }
  }
}
