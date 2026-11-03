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
        path: 'create',
        loadComponent: () => import('./ticket-create/ticket-create.component').then((c) => c.TicketCreateComponent),
        data: { roles: [Role.Admin] }
      },
      {
        path: 'list',
        loadComponent: () => import('./ticket-list/ticket-list.component').then((c) => c.TicketListComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'details',
        loadComponent: () => import('./ticket-details/ticket-details.component').then((c) => c.TicketDetailsComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class HelpdeskTicketRoutingModule {}
