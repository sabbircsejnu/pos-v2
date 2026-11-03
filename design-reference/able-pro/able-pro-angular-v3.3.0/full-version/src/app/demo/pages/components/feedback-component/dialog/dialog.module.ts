// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { DialogRoutingModule } from './dialog-routing.module';
import {
  DialogComponent,
  DialogAnimationsComponent,
  DialogAnimationsScrollComponent,
  DialogAnimationsInjectingDataComponent,
  DialogAnimationsElementComponent
} from './dialog.component';
import { MatNativeDateModule } from '@angular/material/core';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { SharedModule } from 'src/app/demo/shared/shared.module';

@NgModule({
  imports: [
    CommonModule,
    DialogRoutingModule,
    MatNativeDateModule,
    SharedModule,
    DialogComponent,
    DialogAnimationsComponent,
    DialogAnimationsScrollComponent,
    DialogAnimationsInjectingDataComponent,
    DialogAnimationsElementComponent
  ],
  providers: [provideHttpClient(withInterceptorsFromDi())]
})
export class DialogModule {}
