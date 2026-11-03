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
        loadComponent: () => import('./invoice-dashboard/invoice-dashboard.component').then((c) => c.InvoiceDashboardComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'create',
        loadComponent: () => import('./invoice-create/invoice-create.component').then((c) => c.InvoiceCreateComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'details',
        loadComponent: () => import('./invoice-details/invoice-details.component').then((c) => c.InvoiceDetailsComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'list',
        loadComponent: () => import('./invoice-list/invoice-list.component').then((c) => c.InvoiceListComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'edit',
        loadComponent: () => import('./invoice-edit/invoice-edit.component').then((c) => c.InvoiceEditComponent),
        data: { roles: [Role.Admin] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class InvoiceRoutingModule {}
