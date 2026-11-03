// angular import
import { Component, ViewEncapsulation } from '@angular/core';
import { FormGroup, FormControl } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatCalendarCellClassFunction } from '@angular/material/datepicker';
import { MatIconModule } from '@angular/material/icon';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ExHeaderComponent } from './ex-header/ex-header.component';

const today = new Date();
const month = today.getMonth();
const year = today.getFullYear();

@Component({
  selector: 'app-date-picker',
  imports: [CommonModule, SharedModule, MatIconModule],
  templateUrl: './date-picker.component.html',
  styleUrls: ['./date-picker.component.scss'],
  encapsulation: ViewEncapsulation.None
})
export class DatePickerComponent {
  // public props
  range = new FormGroup({
    start: new FormControl<Date | null>(null),
    end: new FormControl<Date | null>(null)
  });
  campaignOne = new FormGroup({
    start: new FormControl(new Date(year, month, 13)),
    end: new FormControl(new Date(year, month, 16))
  });
  campaignTwo = new FormGroup({
    start: new FormControl(new Date(year, month, 15)),
    end: new FormControl(new Date(year, month, 19))
  });
  exampleHeader = ExHeaderComponent;

  // public method
  dateClass: MatCalendarCellClassFunction<Date> = (cellDate, view) => {
    // Only highligh dates inside the month view.
    if (view === 'month') {
      const date = cellDate.getDate();

      // Highlight the 1st and 20th day of each month.
      return date === 1 || date === 20 ? 'example-custom-date-class' : '';
    }
    return '';
  };
}
