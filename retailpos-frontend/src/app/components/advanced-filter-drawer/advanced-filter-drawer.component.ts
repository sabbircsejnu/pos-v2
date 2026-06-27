import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-advanced-filter-drawer',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-40 bg-black/40" (click)="closed.emit()"></div>

    <aside class="fixed top-0 right-0 z-50 h-full w-full md:w-[640px] bg-white shadow-2xl overflow-y-auto">
      <header class="px-5 py-3 border-b flex items-center justify-between sticky top-0 bg-white z-10">
        <h2 class="text-lg font-semibold">{{ title }}</h2>
        <button type="button" class="text-2xl leading-none px-2" (click)="closed.emit()" aria-label="Close Drawer">×</button>
      </header>

      <section class="p-5">
        <ng-content></ng-content>
      </section>

      <footer class="sticky bottom-0 bg-white border-t px-5 py-3">
        <div class="flex flex-col-reverse sm:flex-row sm:items-center sm:justify-end gap-2">
          <button type="button" class="btn btn-outline" (click)="closed.emit()">{{ closeLabel }}</button>
          <button type="button" class="btn btn-outline" (click)="clear.emit()">{{ clearLabel }}</button>
          <button type="button" class="btn btn-primary" (click)="apply.emit()">{{ applyLabel }}</button>
        </div>
      </footer>
    </aside>
  `,
})
export class AdvancedFilterDrawerComponent {
  @Input() title = 'Advanced Filters';
  @Input() applyLabel = 'Apply Filters';
  @Input() clearLabel = 'Clear Filters';
  @Input() closeLabel = 'Close Drawer';

  @Output() closed = new EventEmitter<void>();
  @Output() clear = new EventEmitter<void>();
  @Output() apply = new EventEmitter<void>();
}
