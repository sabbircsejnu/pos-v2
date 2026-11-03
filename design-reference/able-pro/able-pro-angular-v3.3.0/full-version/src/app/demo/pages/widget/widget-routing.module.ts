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
        path: 'statistics',
        loadComponent: () => import('./statistics/statistics.component').then((c) => c.StatisticsComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'data',
        loadComponent: () => import('./widget-data/widget-data.component').then((c) => c.WidgetDataComponent),
        data: { roles: [Role.Admin, Role.User] }
      },
      {
        path: 'chart',
        loadComponent: () => import('./chart/chart.component').then((c) => c.WidgetChartComponent),
        data: { roles: [Role.Admin, Role.User] }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class WidgetRoutingModule {}
