// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// project import
import { AccountProfileComponent } from './account-profile.component';

// type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    component: AccountProfileComponent,
    children: [
      {
        path: '',
        redirectTo: '/application/profile/account/profile',
        pathMatch: 'full'
      },
      {
        path: 'profile',
        loadComponent: () => import('./ac-profile/ac-profile.component').then((c) => c.AcProfileComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'personal',
        loadComponent: () => import('./ac-personal/ac-personal.component').then((c) => c.AcPersonalComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'account',
        loadComponent: () => import('./ac-account/ac-account.component').then((c) => c.AcAccountComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'password',
        loadComponent: () => import('./ac-password/ac-password.component').then((c) => c.AcPasswordComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'role',
        loadComponent: () => import('./ac-role/ac-role.component').then((c) => c.AcRoleComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'settings',
        loadComponent: () => import('./ac-setting/ac-setting.component').then((c) => c.AcSettingComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class AccountProfileRoutingModule {}
