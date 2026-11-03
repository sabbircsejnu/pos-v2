// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

//type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'dashboard',
        loadComponent: () => import('./membership-dashboard/membership-dashboard.component').then((c) => c.MembershipDashboardComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'list',
        loadComponent: () => import('./membership-list/membership-list.component').then((c) => c.MembershipListComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'price',
        loadComponent: () => import('./membership-price/membership-price.component').then((c) => c.MembershipPriceComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'setting',
        loadComponent: () => import('./membership-setting/membership-setting.component').then((c) => c.MembershipSettingComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class MembershipRoutingModule {}
