import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { Role } from 'src/app/@theme/types/role';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'basic',
        loadComponent: () => import('./basic-table/basic-table').then((c) => c.BasicTable),
        data: {
          roles: [Role.Admin, Role.User]
        }
      },
      {
        path: 'column-moving',
        loadComponent: () => import('./column-moving/column-moving').then((c) => c.ColumnMoving),
        data: {
          roles: [Role.Admin, Role.User]
        }
      },
      {
        path: 'row-pagination',
        loadComponent: () => import('./row-pagination/row-pagination').then((c) => c.RowPagination),
        data: {
          roles: [Role.Admin, Role.User]
        }
      },
      {
        path: 'highlight-change',
        loadComponent: () => import('./highlighting-change/highlighting-change').then((c) => c.HighlightingChange),
        data: {
          roles: [Role.Admin, Role.User]
        }
      },
      {
        path: 'column-filter',
        loadComponent: () => import('./column-filter/column-filter').then((c) => c.ColumnFilter),
        data: {
          roles: [Role.Admin, Role.User]
        }
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class AgGridTableRoutingModule {}
