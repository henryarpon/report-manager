import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { CellValue, ColumnHeader, FieldType, ReportPage, ReportSort } from './report.models';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

interface DisplayCell {
  text: string;
  empty: boolean;
}

/**
 * Formats a cell for display. Dates arrive as Manila local `yyyy-MM-ddTHH:mm:ss` and are formatted
 * from the string itself (no time zone conversion): "31 Oct 2026, 21:45"; midnight shows the day only.
 */
function formatCell(value: CellValue, type: FieldType): DisplayCell {
  if (value === null || value === undefined || value === '') {
    return { text: '—', empty: true };
  }
  if (type === 'date' && typeof value === 'string' && value.length >= 16) {
    const [year, month, day] = value.slice(0, 10).split('-');
    const time = value.slice(11, 16);
    const date = `${Number(day)} ${MONTHS[Number(month) - 1] ?? month} ${year}`;
    return { text: time === '00:00' ? date : `${date}, ${time}`, empty: false };
  }
  if (type === 'number' && typeof value === 'number') {
    return { text: value.toLocaleString(), empty: false };
  }
  return { text: String(value), empty: false };
}

interface DisplayColumn extends ColumnHeader {
  index: number;
  sortable: boolean;
}

/** Server-side paged and sorted report table. Rows are positional arrays matching `page.columns`. */
@Component({
  selector: 'app-report-table',
  imports: [MatButtonModule, MatIconModule, MatTableModule, MatSortModule, MatPaginatorModule],
  host: { '[class.loading]': 'loading()' },
  template: `
    <div class="table-scroll" role="region" tabindex="0" [attr.aria-label]="caption() + ', scrollable'">
      <table
        mat-table
        [dataSource]="rows()"
        matSort
        matSortDisableClear
        [matSortActive]="sort().column"
        [matSortDirection]="sort().dir"
        (matSortChange)="changeSort($event)"
      >
        <caption class="visually-hidden">{{ caption() }}</caption>
        @for (column of columns(); track column.key) {
          <ng-container [matColumnDef]="column.key">
            <th
              mat-header-cell
              *matHeaderCellDef
              mat-sort-header
              [disabled]="!column.sortable"
              [class.numeric]="column.type === 'number'"
            >
              {{ column.label }}
            </th>
            <td
              mat-cell
              *matCellDef="let row"
              [class.numeric]="column.type === 'number'"
              [class.date]="column.type === 'date'"
              [class.empty]="row.cells[column.index].empty"
            >
              @if (row.cells[column.index].empty) {
                <span aria-hidden="true">—</span><span class="visually-hidden">Empty</span>
              } @else {
                <span class="cell-text" [title]="row.cells[column.index].text">{{ row.cells[column.index].text }}</span>
              }
            </td>
          </ng-container>
        }
        <tr mat-header-row *matHeaderRowDef="columnKeys(); sticky: true"></tr>
        <tr mat-row *matRowDef="let row; columns: columnKeys()"></tr>
        <tr class="mat-mdc-row" *matNoDataRow>
          <td class="mat-mdc-cell no-data" [attr.colspan]="columnKeys().length">
            <div class="empty-state">
              <mat-icon aria-hidden="true">filter_alt_off</mat-icon>
              <p class="empty-title">No rows match these filters</p>
              <p class="muted">Try a wider date range, fewer values, or remove a filter.</p>
              @if (hasFilters()) {
                <button mat-stroked-button type="button" (click)="clearFilters.emit()">Clear all filters</button>
              }
            </div>
          </td>
        </tr>
      </table>
    </div>
    <mat-paginator
      [length]="page().totalCount"
      [pageIndex]="page().page - 1"
      [pageSize]="page().pageSize"
      [pageSizeOptions]="pageSizeOptions()"
      showFirstLastButtons
      aria-label="Report pages"
      (page)="pageChange.emit($event)"
    />
  `,
})
export class ReportTable {
  readonly page = input.required<ReportPage>();
  readonly sort = input.required<ReportSort>();
  readonly sortableKeys = input.required<ReadonlySet<string>>();
  readonly pageSizeOptions = input<number[]>([10, 25, 50, 100]);
  readonly caption = input('Report results');
  readonly loading = input(false);
  readonly hasFilters = input(false);

  readonly sortChange = output<ReportSort>();
  readonly pageChange = output<PageEvent>();
  readonly clearFilters = output<void>();

  protected readonly columns = computed<DisplayColumn[]>(() =>
    this.page().columns.map((c, index) => ({ ...c, index, sortable: this.sortableKeys().has(c.key) })),
  );

  protected readonly columnKeys = computed(() => this.columns().map((c) => c.key));

  protected readonly rows = computed(() => {
    const columns = this.page().columns;
    return this.page().items.map((item) => ({ cells: item.map((value, i) => formatCell(value, columns[i].type)) }));
  });

  protected changeSort(sort: Sort): void {
    if (sort.direction) {
      this.sortChange.emit({ column: sort.active, dir: sort.direction });
    }
  }
}
