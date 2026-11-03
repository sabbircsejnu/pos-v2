import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'dialog',
        loadChildren: () => import('./dialog/dialog.module').then((e) => e.DialogModule)
      },
      {
        path: 'progress-bar',
        loadComponent: () => import('./progress-bar/progress-bar.component').then((c) => c.ProgressBarComponent)
      },
      {
        path: 'snackbar',
        loadComponent: () => import('./snackbar/snackbar.component').then((c) => c.SnackbarComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class FeedbackComponentRoutingModule {}
