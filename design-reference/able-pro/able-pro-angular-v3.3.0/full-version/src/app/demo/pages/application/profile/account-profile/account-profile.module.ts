// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { AccountProfileRoutingModule } from './account-profile-routing.module';
import { AccountProfileComponent } from './account-profile.component';
import { SharedModule } from 'src/app/demo/shared/shared.module';

@NgModule({
  imports: [CommonModule, SharedModule, AccountProfileRoutingModule, AccountProfileComponent]
})
export class AccountProfileModule {}
