// angular import
import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

// Type
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'layout',
        loadChildren: () => import('./frm-layouts/frm-layouts.module').then((m) => m.FrmLayoutsModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'plugins',
        loadChildren: () => import('./frm-plugins/frm-plugins.module').then((m) => m.FrmPluginsModule),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'validation',
        loadComponent: () => import('./forms-validation/forms-validation.component').then((c) => c.FormsValidationComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'wizard',
        loadComponent: () => import('./forms-wizard/forms-wizard.component').then((c) => c.FormsWizardComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class FormsRoutingModule {}
