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
        path: 'chat',
        loadComponent: () => import('./chat/chat.component').then((c) => c.ChatComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'kanban',
        loadComponent: () => import('./kanban/kanban.component').then((c) => c.KanbanComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'calender',
        loadChildren: () => import('./calender/calender.module').then((m) => m.CalenderModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'customer',
        loadComponent: () => import('./customer-list/customer-list.component').then((c) => c.CustomerListComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'profile',
        loadChildren: () => import('./profile/profile.module').then((m) => m.ProfileModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'e-commerce',
        loadChildren: () => import('./e-commerce/e-commerce.module').then((m) => m.ECommerceModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'file-manager',
        loadComponent: () => import('./file-manager/file-manager.component').then((c) => c.FileManagerComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'email',
        loadComponent: () => import('./email/email.component').then((c) => c.EmailComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class ApplicationRoutingModule {}
