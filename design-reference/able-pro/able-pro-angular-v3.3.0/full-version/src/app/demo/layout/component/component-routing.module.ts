import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { ComponentComponent } from './component.component';

const routes: Routes = [
  {
    path: '',
    component: ComponentComponent,
    children: [
      {
        path: '',
        redirectTo: '/components/input/button',
        pathMatch: 'full'
      },
      {
        path: 'input',
        loadChildren: () => import('../../pages/components/input-component/input-component.module').then((m) => m.InputComponentModule)
      },
      {
        path: 'd-display',
        loadChildren: () => import('../../pages/components/data-display/data-display.module').then((m) => m.DataDisplayModule)
      },
      {
        path: 'feedback',
        loadChildren: () =>
          import('../../pages/components/feedback-component/feedback-component.module').then((m) => m.FeedbackComponentModule)
      },
      {
        path: 'navigation',
        loadChildren: () =>
          import('../../pages/components/navigation-component/navigation-component.module').then((m) => m.NavigationComponentModule)
      },
      {
        path: 'surface',
        loadChildren: () =>
          import('../../pages/components/surface-component/surface-component.module').then((m) => m.SurfaceComponentModule)
      },
      {
        path: 'utils',
        loadChildren: () => import('../../pages/components/utils/utils.module').then((m) => m.UtilsModule)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class ComponentRoutingModule {}
