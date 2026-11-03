import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'badge',
        loadComponent: () => import('./badge/badge.component').then((c) => c.BadgeComponent)
      },
      {
        path: 'list',
        loadComponent: () => import('./list/list.component').then((c) => c.ListComponent)
      },

      {
        path: 'chips',
        loadComponent: () => import('./chips/chips.component').then((c) => c.ChipsComponent)
      },
      {
        path: 'tooltip',
        loadComponent: () => import('./tooltip/tooltip.component').then((c) => c.TooltipComponent)
      },
      {
        path: 'typography',
        loadComponent: () => import('./typography/typography.component').then((c) => c.TypographyComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class DataDisplayRoutingModule {}
