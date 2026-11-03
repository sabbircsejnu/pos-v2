import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'card',
        loadComponent: () => import('./cards-basic/cards-basic.component').then((c) => c.CardsBasicComponent)
      },
      {
        path: 'accordion',
        loadComponent: () => import('./accordion/accordion.component').then((c) => c.AccordionComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class SurfaceComponentRoutingModule {}
