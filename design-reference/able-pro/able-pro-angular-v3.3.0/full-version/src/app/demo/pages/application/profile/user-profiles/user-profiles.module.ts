// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { UserProfilesRoutingModule } from './user-profiles-routing.module';
import { UserProfilesComponent } from './user-profiles.component';
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { TopSvgComponent } from './top-svg/top-svg.component';
import { BackSvgComponent } from './back-svg/back-svg.component';

// third party
import { NgApexchartsModule } from 'ng-apexcharts';

@NgModule({
  imports: [
    CommonModule,
    UserProfilesRoutingModule,
    NgApexchartsModule,
    SharedModule,
    TopSvgComponent,
    BackSvgComponent,
    UserProfilesComponent
  ]
})
export class UserProfilesModule {}
