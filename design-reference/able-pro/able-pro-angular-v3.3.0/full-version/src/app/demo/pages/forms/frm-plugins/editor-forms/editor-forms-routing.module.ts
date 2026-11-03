import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { EditorFormsComponent } from './editor-forms.component';

const routes: Routes = [
  {
    path: '',
    component: EditorFormsComponent
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class EditorFormsRoutingModule {}
