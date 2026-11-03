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
        loadComponent: () => import('./helpdesk-dashboard/helpdesk-dashboard.component').then((c) => c.HelpdeskDashboardComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'ticket',
        loadChildren: () => import('./helpdesk-ticket/helpdesk-ticket.module').then((m) => m.HelpdeskTicketModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'customer',
        loadComponent: () => import('./helpdesk-customer/helpdesk-customer.component').then((c) => c.HelpdeskCustomerComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class HelpdeskRoutingModule {}
