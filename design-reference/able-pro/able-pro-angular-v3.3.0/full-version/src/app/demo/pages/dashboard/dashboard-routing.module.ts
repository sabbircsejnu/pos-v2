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
        path: 'default',
        loadComponent: () => import('./default/default.component').then((c) => c.DefaultComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'analytics',
        loadComponent: () => import('./analytics/analytics.component').then((c) => c.AnalyticsComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'finance',
        loadComponent: () => import('./finance/finance.component').then((c) => c.FinanceComponent),
        data: { roles: [Role.Admin] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class DashboardRoutingModule {}
