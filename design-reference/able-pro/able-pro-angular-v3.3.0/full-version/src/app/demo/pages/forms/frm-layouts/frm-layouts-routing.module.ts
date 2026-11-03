import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'basic-layout',
        loadComponent: () => import('./basic-forms/basic-forms.component').then((c) => c.BasicFormsComponent)
      },
      {
        path: 'multi-layout',
        loadComponent: () => import('./multi-column/multi-column.component').then((c) => c.MultiColumnComponent)
      },
      {
        path: 'action-bar',
        loadComponent: () => import('./action-bar/action-bar.component').then((c) => c.ActionBarComponent)
      },
      {
        path: 'sticky-bar',
        loadComponent: () => import('./sticky-form/sticky-form.component').then((c) => c.StickyFormComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class FrmLayoutsRoutingModule {}
