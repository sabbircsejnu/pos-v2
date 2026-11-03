import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'error-404',
        loadComponent: () => import('./error/error.component').then((c) => c.ErrorComponent)
      },
      {
        path: 'error-500',
        loadComponent: () => import('./error-two/error-two.component').then((c) => c.ErrorTwoComponent)
      },
      {
        path: 'under-constructor',
        loadComponent: () => import('./under-constructor/under-constructor.component').then((c) => c.UnderConstructorComponent)
      },
      {
        path: 'coming-soon',
        loadComponent: () => import('./coming-soon/coming-soon.component').then((c) => c.ComingSoonComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class MaintenanceRoutingModule {}
