// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// project import
import { UserProfilesComponent } from './user-profiles.component';

// type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    component: UserProfilesComponent,
    children: [
      {
        path: '',
        redirectTo: '/application/profile/user/personal',
        pathMatch: 'full'
      },
      {
        path: 'personal',
        loadComponent: () => import('./us-personal/us-personal.component').then((c) => c.UsPersonalComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'payment',
        loadComponent: () => import('./us-payment/us-payment.component').then((c) => c.UsPaymentComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'password',
        loadComponent: () => import('./us-password/us-password.component').then((c) => c.UsPasswordComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'setting',
        loadComponent: () => import('./us-setting/us-setting.component').then((c) => c.UsSettingComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class UserProfilesRoutingModule {}
