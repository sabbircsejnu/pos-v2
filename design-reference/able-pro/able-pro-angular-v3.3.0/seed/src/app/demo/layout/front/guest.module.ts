// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';

// project import
import { GuestComponent } from './guest.component';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ConfigurationComponent } from 'src/app/@theme/layouts/configuration/configuration.component';

@NgModule({
  imports: [CommonModule, SharedModule, RouterModule, ConfigurationComponent, GuestComponent]
})
export class GuestModule {}
