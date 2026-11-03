// angular import
import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnDestroy, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatCalendar } from '@angular/material/datepicker';
import { DateAdapter, MAT_DATE_FORMATS, MatDateFormats } from '@angular/material/core';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ex-header',
  imports: [CommonModule, SharedModule],
  templateUrl: './ex-header.component.html',
  styleUrls: ['./ex-header.component.scss'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class ExHeaderComponent<D> implements OnDestroy {
  private _calendar = inject<MatCalendar<D>>(MatCalendar);
  private _dateAdapter = inject<DateAdapter<D>>(DateAdapter);
  private _dateFormats = inject<MatDateFormats>(MAT_DATE_FORMATS);

  private _destroyed = new Subject<void>();

  constructor() {
    const _calendar = this._calendar;
    const cdr = inject(ChangeDetectorRef);

    _calendar.stateChanges.pipe(takeUntil(this._destroyed)).subscribe(() => cdr.markForCheck());
  }

  ngOnDestroy() {
    this._destroyed.next();
    this._destroyed.complete();
  }

  get periodLabel() {
    return this._dateAdapter.format(this._calendar.activeDate, this._dateFormats.display.monthYearLabel).toLocaleUpperCase();
  }

  previousClicked(mode: 'month' | 'year') {
    this._calendar.activeDate =
      mode === 'month'
        ? this._dateAdapter.addCalendarMonths(this._calendar.activeDate, -1)
        : this._dateAdapter.addCalendarYears(this._calendar.activeDate, -1);
  }

  nextClicked(mode: 'month' | 'year') {
    this._calendar.activeDate =
      mode === 'month'
        ? this._dateAdapter.addCalendarMonths(this._calendar.activeDate, 1)
        : this._dateAdapter.addCalendarYears(this._calendar.activeDate, 1);
  }
}
