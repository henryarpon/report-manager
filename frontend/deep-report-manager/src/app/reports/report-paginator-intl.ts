import { Injectable } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';

/** Paginator labels with thousands separators ("26–50 of 55,028"). Provided by the reports page. */
@Injectable()
export class ReportPaginatorIntl extends MatPaginatorIntl {
  override itemsPerPageLabel = 'Rows per page';

  override getRangeLabel = (page: number, pageSize: number, length: number): string => {
    if (length === 0) {
      return '0 of 0';
    }
    const start = page * pageSize + 1;
    const end = Math.min((page + 1) * pageSize, length);
    return `${start.toLocaleString()}–${end.toLocaleString()} of ${length.toLocaleString()}`;
  };
}
