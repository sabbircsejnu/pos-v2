// Angular Imports
import { Component, TemplateRef, OnDestroy, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';

// Angular Material
import { MatButton } from '@angular/material/button';
import { MatFormField } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';

// Third party
import { startOfDay, endOfDay, subDays, addDays, endOfMonth, isSameDay, isSameMonth, addHours } from 'date-fns';
import {
  CalendarEvent,
  CalendarEventTimesChangedEvent,
  CalendarView,
  CalendarCommonModule,
  CalendarMonthModule,
  CalendarWeekModule,
  CalendarDayModule
} from 'angular-calendar';
import { EventColor } from 'calendar-utils';
import { Subject } from 'rxjs';
import { FlatpickrDirective } from 'angularx-flatpickr';

// project
import { CardComponent } from 'src/app/@theme/components/card/card.component';

@Component({
  selector: 'app-calender',
  templateUrl: './calender.component.html',
  styleUrls: ['./calender.component.scss'],
  imports: [
    CardComponent,
    MatButton,
    CalendarCommonModule,
    CalendarMonthModule,
    CalendarWeekModule,
    CalendarDayModule,
    MatFormField,
    MatInput,
    FormsModule,
    FlatpickrDirective
  ]
})
export class CalenderComponent implements OnDestroy {
  // Private props
  colors: Record<string, EventColor> = {
    red: {
      primary: '#dc2626',
      secondary: '#EBC2C2'
    },
    blue: {
      primary: '#4680ff',
      secondary: '#90b3ff'
    },
    yellow: {
      primary: '#e58a00',
      secondary: '#edad4d'
    }
  };

  readonly modalContent = viewChild.required<TemplateRef<string>>('modalContent');
  view: CalendarView = CalendarView.Month;
  calendarView = CalendarView;
  viewDate: Date = new Date();
  activeDayIsOpen = true;
  refresh = new Subject<void>();

  // Public methods
  events: CalendarEvent[] = [
    {
      start: subDays(startOfDay(new Date()), 1),
      end: addDays(new Date(), 1),
      title: 'A 3 day event',
      color: { ...this.colors['red'] }
    },
    {
      start: startOfDay(new Date()),
      title: 'An event with no end date',
      color: { ...this.colors['yellow'] }
    },
    {
      start: subDays(endOfMonth(new Date()), 3),
      end: addDays(endOfMonth(new Date()), 3),
      title: 'A long event that spans 2 months',
      color: { ...this.colors['blue'] },
      allDay: true
    },
    {
      start: addHours(startOfDay(new Date()), 2),
      end: addHours(new Date(), 2),
      title: 'A draggable and resizable event',
      color: { ...this.colors['yellow'] }
    }
  ];

  dayClicked({ date, events }: { date: Date; events: CalendarEvent[] }): void {
    if (isSameMonth(date, this.viewDate)) {
      if ((isSameDay(this.viewDate, date) && this.activeDayIsOpen === true) || events.length === 0) {
        this.activeDayIsOpen = false;
      } else {
        this.activeDayIsOpen = true;
      }
      this.viewDate = date;
    }
  }

  eventTimesChanged({ event, newStart, newEnd }: CalendarEventTimesChangedEvent): void {
    this.events = this.events.map((iEvent) => {
      if (iEvent === event) {
        return {
          ...event,
          start: newStart,
          end: newEnd
        };
      }
      return iEvent;
    });
  }

  addEvent(): void {
    this.events = [
      ...this.events,
      {
        title: 'New event',
        start: startOfDay(new Date()),
        end: endOfDay(new Date()),
        color: this.colors['red'],
        draggable: true,
        resizable: {
          beforeStart: true,
          afterEnd: true
        }
      }
    ];
  }

  deleteEvent(eventToDelete: CalendarEvent) {
    this.events = this.events.filter((event) => event !== eventToDelete);
  }

  setView(view: CalendarView) {
    this.view = view;
  }

  closeOpenMonthViewDay() {
    this.activeDayIsOpen = false;
  }

  // life cycle event
  ngOnDestroy() {
    this.refresh.next();
    this.refresh.complete();
  }
}
