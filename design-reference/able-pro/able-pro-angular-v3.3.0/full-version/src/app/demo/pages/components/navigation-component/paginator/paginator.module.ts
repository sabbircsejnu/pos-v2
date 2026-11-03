// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { PaginatorRoutingModule } from './paginator-routing.module';
import { PaginatorComponent } from './paginator.component';
import { MatNativeDateModule } from '@angular/material/core';
import { provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { SharedModule } from 'src/app/demo/shared/shared.module';

@NgModule({
  imports: [CommonModule, PaginatorRoutingModule, SharedModule, MatNativeDateModule, PaginatorComponent],
  providers: [provideHttpClient(withInterceptorsFromDi())]
})
export class PaginatorModule {}
