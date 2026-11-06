import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Service } from '@angular/core';
import { Observable, shareReplay, tap } from 'rxjs';
import { ApiError, LookupItem, LookupSource, ReportDefinition } from './report.models';

const BASE = '/api/reports';

/** Thin client for the `/api/reports` endpoints that are not loaded through `httpResource`. */
@Service()
export class ReportsApi {
  private readonly http = inject(HttpClient);

  readonly catalogUrl = `${BASE}/catalog`;
  readonly runUrl = `${BASE}/run`;

  lookup(source: LookupSource, search = '', programIds: readonly number[] = []): Observable<LookupItem[]> {
    let params = new HttpParams();
    if (search) {
      params = params.set('search', search);
    }
    for (const id of programIds) {
      params = params.append('programIds', id);
    }
    return this.http.get<LookupItem[]>(`${BASE}/lookups/${source}`, { params });
  }

  /** Small, fixed option lists (provinces, focus zones), fetched once per app session. */
  staticLookup(source: LookupSource): Observable<LookupItem[]> {
    let cached = this.staticLookups.get(source);
    if (!cached) {
      cached = this.lookup(source).pipe(
        // A failed load is not cached, so the next use retries.
        tap({ error: () => this.staticLookups.delete(source) }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.staticLookups.set(source, cached);
    }
    return cached;
  }

  private readonly staticLookups = new Map<LookupSource, Observable<LookupItem[]>>();

  /** Full response so the caller can read the file name from `Content-Disposition`. */
  export(definition: ReportDefinition) {
    return this.http.post(`${BASE}/export`, definition, { observe: 'response', responseType: 'blob' });
  }
}

/** Best-effort user-facing message from an API failure (JSON or, for downloads, a JSON blob). */
export async function errorMessage(error: unknown): Promise<string> {
  if (error instanceof HttpErrorResponse) {
    let body: unknown = error.error;
    if (body instanceof Blob) {
      try {
        body = JSON.parse(await body.text());
      } catch {
        body = null;
      }
    }
    return messageFromBody(body) ?? fallbackMessage(error);
  }
  return 'Something went wrong. Try again.';
}

export function syncErrorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    return messageFromBody(error.error) ?? fallbackMessage(error);
  }
  return 'Something went wrong. Try again.';
}

function messageFromBody(body: unknown): string | null {
  if (!body || typeof body !== 'object' || !('message' in body)) {
    return null;
  }
  const apiError = body as ApiError;
  const details = Object.values(apiError.errors ?? {}).flat();
  const reference = apiError.referenceKey ? ` (reference ${apiError.referenceKey})` : '';
  return details.length ? `${apiError.message} ${details.join(' ')}` : `${apiError.message}${reference}`;
}

function fallbackMessage(error: HttpErrorResponse): string {
  return error.status === 0
    ? 'Cannot reach the reports service. Check that the API is running.'
    : `Request failed (${error.status}).`;
}
