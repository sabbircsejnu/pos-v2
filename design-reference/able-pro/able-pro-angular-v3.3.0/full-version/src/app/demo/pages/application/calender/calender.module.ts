// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { CalenderRoutingModule } from './calender-routing.module';
import { CalenderComponent } from './calender.component';
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { CalendarModule, DateAdapter } from 'angular-calendar';
import { adapterFactory } from 'angular-calendar/date-adapters/date-fns';
import { FlatpickrDirective, provideFlatpickrDefaults } from 'angularx-flatpickr';

@NgModule({
  imports: [
    CommonModule,
    CalenderRoutingModule,
    SharedModule,
    FlatpickrDirective,
    CalendarModule.forRoot({
      provide: DateAdapter,
      useFactory: adapterFactory
    }),
    CalenderComponent
  ],
  providers: [provideFlatpickrDefaults()]
})
export class CalenderModule {}
