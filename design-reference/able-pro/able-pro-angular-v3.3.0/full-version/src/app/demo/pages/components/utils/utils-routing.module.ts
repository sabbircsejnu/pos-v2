import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'color',
        loadComponent: () => import('./colors-component/colors-component.component').then((c) => c.ColorsComponentComponent)
      },
      {
        path: 'date-picker',
        loadComponent: () => import('./date-picker/date-picker.component').then((c) => c.DatePickerComponent)
      },
      {
        path: 'tree',
        loadComponent: () => import('./tree-view/tree-view.component').then((c) => c.TreeViewComponent)
      },
      {
        path: 'drag-drop',
        loadComponent: () => import('./drag-drop/drag-drop.component').then((c) => c.DragDropComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class UtilsRoutingModule {}
