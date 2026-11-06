/** Shapes returned by `GET /api/reports/catalog` and the other `/api/reports` endpoints. */

export type RootKey = 'transaction' | 'customer' | 'program';
export type FieldType = 'string' | 'date' | 'enum' | 'lookup' | 'number';
export type SortDir = 'asc' | 'desc';
export type LookupSource = 'customers' | 'programs' | 'outlets' | 'provinces' | 'focus-zones';

export interface CatalogColumn {
  key: string;
  label: string;
  type: FieldType;
  sortable: boolean;
  default: boolean;
  sensitive: boolean;
}

export interface ReportSort {
  column: string;
  dir: SortDir;
}

export interface CatalogRoot {
  key: RootKey;
  label: string;
  columns: CatalogColumn[];
  defaultSort: ReportSort;
  /** Per filter entity, how its filters relate to this root ("Has a transaction matching"). */
  filterScopes: Record<string, string>;
}

export interface EnumOption {
  value: number;
  label: string;
}

export interface CatalogFilter {
  key: string;
  entity: string;
  field: string;
  label: string;
  type: FieldType;
  operators: string[];
  options: EnumOption[] | null;
  lookup: LookupSource | null;
  dependsOn: string | null;
}

export interface Catalog {
  roots: CatalogRoot[];
  filters: CatalogFilter[];
  defaultPageSize: number;
  maxPageSize: number;
}

export interface LookupItem {
  value: string | number;
  label: string;
  parentId: number | null;
}

/** One filter as sent to the API. */
export interface FilterRequest {
  entity: string;
  field: string;
  op: string;
  value: unknown;
}

export interface ReportDefinition {
  root: RootKey;
  columns: string[];
  filters: FilterRequest[];
  sort: ReportSort;
}

export interface RunReportRequest extends ReportDefinition {
  page: number;
  pageSize: number;
}

export interface ColumnHeader {
  key: string;
  label: string;
  type: FieldType;
}

export type CellValue = string | number | boolean | null;

export interface ReportPage {
  columns: ColumnHeader[];
  /** Positional rows matching `columns`. */
  items: CellValue[][];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** Body of a 422 / 500 response. */
export interface ApiError {
  message: string;
  referenceKey?: string | null;
  errors?: Record<string, string[]> | null;
}
