import { CdkTrapFocus } from '@angular/cdk/a11y';
import { CdkConnectedOverlay, CdkOverlayOrigin, ConnectedPosition } from '@angular/cdk/overlay';
import { Component, computed, input, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { CatalogColumn } from '../report.models';

export const POPOVER_POSITIONS: ConnectedPosition[] = [
  { originX: 'start', originY: 'bottom', overlayX: 'start', overlayY: 'top', offsetY: 6 },
  { originX: 'start', originY: 'top', overlayX: 'start', overlayY: 'bottom', offsetY: -6 },
  { originX: 'end', originY: 'bottom', overlayX: 'end', overlayY: 'top', offsetY: 6 },
];

/** "Customer.EmailAddress" → "Customer". Aggregate columns sit with their root entity. */
function columnEntity(key: string): string {
  return key.split('.')[0];
}

/**
 * Column chooser: the picked columns as removable tags, plus a popover checklist
 * grouped by entity with a search box. Order always follows the catalog.
 */
@Component({
  selector: 'app-column-picker',
  imports: [CdkConnectedOverlay, CdkOverlayOrigin, CdkTrapFocus, MatButtonModule, MatCheckboxModule, MatIconModule],
  template: `
    <div class="column-summary">
      @if (selected().length) {
        <ul class="tag-list" aria-label="Columns shown">
          @for (column of selectedColumns(); track column.key) {
            <li class="tag">
              <span class="tag-label">{{ column.label }}</span>
              <button type="button" class="tag-remove" [attr.aria-label]="'Hide column ' + column.label" (click)="toggle(column.key)">
                <mat-icon aria-hidden="true">close</mat-icon>
              </button>
            </li>
          }
        </ul>
      } @else {
        <p class="muted picker-note">No columns picked, so the defaults are shown.</p>
      }
      <button
        mat-stroked-button
        type="button"
        class="picker-trigger"
        cdkOverlayOrigin
        #origin="cdkOverlayOrigin"
        aria-haspopup="dialog"
        [attr.aria-expanded]="open()"
        (click)="open.set(!open())"
      >
        <mat-icon aria-hidden="true">view_column</mat-icon>
        Choose columns
        <span class="trigger-count">{{ selected().length }} of {{ columns().length }}</span>
      </button>
    </div>

    <ng-template
      cdkConnectedOverlay
      [cdkConnectedOverlayOrigin]="origin"
      [cdkConnectedOverlayOpen]="open()"
      [cdkConnectedOverlayPositions]="positions"
      cdkConnectedOverlayHasBackdrop
      cdkConnectedOverlayFlexibleDimensions
      [cdkConnectedOverlayViewportMargin]="12"
      cdkConnectedOverlayBackdropClass="cdk-overlay-transparent-backdrop"
      (backdropClick)="close()"
      (overlayKeydown)="$event.key === 'Escape' && close()"
      (detach)="close()"
    >
      <div class="popover" role="dialog" aria-label="Choose columns" cdkTrapFocus [cdkTrapFocusAutoCapture]="true">
        <div class="popover-search">
          <mat-icon aria-hidden="true">search</mat-icon>
          <input
            #searchBox
            type="search"
            placeholder="Search columns"
            aria-label="Search columns"
            [value]="search()"
            (input)="search.set(searchBox.value)"
          />
        </div>
        <div class="popover-body">
          @for (group of groups(); track group.entity) {
            <fieldset class="popover-group">
              <legend>{{ group.entity }}</legend>
              @for (column of group.columns; track column.key) {
                <mat-checkbox [checked]="isSelected(column.key)" (change)="toggle(column.key)">
                  {{ column.label }}
                </mat-checkbox>
              }
            </fieldset>
          } @empty {
            <p class="muted popover-empty">No column matches “{{ search() }}”.</p>
          }
        </div>
        <div class="popover-foot">
          <button mat-button type="button" (click)="useDefaults()">Use defaults</button>
          <button mat-button type="button" (click)="selectAll()">Show all</button>
          <button mat-flat-button type="button" class="popover-done" (click)="close()">Done</button>
        </div>
      </div>
    </ng-template>
  `,
})
export class ColumnPicker {
  readonly columns = input.required<CatalogColumn[]>();
  readonly selected = input.required<string[]>();
  readonly selectedChange = output<string[]>();

  protected readonly open = signal(false);
  protected readonly search = signal('');
  protected readonly positions = POPOVER_POSITIONS;

  private readonly selectedSet = computed(() => new Set(this.selected()));

  protected readonly selectedColumns = computed(() => this.columns().filter((c) => this.selectedSet().has(c.key)));

  protected readonly groups = computed(() => {
    const term = this.search().trim().toLowerCase();
    const groups = new Map<string, CatalogColumn[]>();
    for (const column of this.columns()) {
      if (term && !column.label.toLowerCase().includes(term)) {
        continue;
      }
      const entity = columnEntity(column.key);
      groups.set(entity, [...(groups.get(entity) ?? []), column]);
    }
    return [...groups].map(([entity, columns]) => ({ entity, columns }));
  });

  protected isSelected(key: string): boolean {
    return this.selectedSet().has(key);
  }

  protected toggle(key: string): void {
    const set = new Set(this.selected());
    if (set.has(key)) {
      set.delete(key);
    } else {
      set.add(key);
    }
    this.selectedChange.emit(this.columns().filter((c) => set.has(c.key)).map((c) => c.key));
  }

  protected useDefaults(): void {
    this.selectedChange.emit(this.columns().filter((c) => c.default).map((c) => c.key));
  }

  protected selectAll(): void {
    this.selectedChange.emit(this.columns().map((c) => c.key));
  }

  protected close(): void {
    this.open.set(false);
    this.search.set('');
  }
}
