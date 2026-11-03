import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'mask',
        loadComponent: () => import('./mask-form/mask-form.component').then((c) => c.MaskFormComponent)
      },
      {
        path: 'reCaptcha',
        loadComponent: () => import('./re-captcha/re-captcha.component').then((c) => c.ReCaptchaComponent)
      },
      {
        path: 'clip-board',
        loadComponent: () => import('./clip-board/clip-board.component').then((c) => c.ClipBoardComponent)
      },
      {
        path: 'editor',
        loadChildren: () => import('./editor-forms/editor-forms.module').then((m) => m.EditorFormsModule)
      },
      {
        path: 'drop-zone',
        loadComponent: () => import('./drop-zone/drop-zone.component').then((c) => c.DropZoneComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class FrmPluginsRoutingModule {}
