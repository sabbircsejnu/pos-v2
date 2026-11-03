// angular import
import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { EditorFormsRoutingModule } from './editor-forms-routing.module';
import { EditorFormsComponent } from './editor-forms.component';
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party

import { NgxEditorModule } from 'ngx-editor';
import { QuillModule } from 'ngx-quill';

@NgModule({
  imports: [CommonModule, EditorFormsRoutingModule, NgxEditorModule, SharedModule, QuillModule.forRoot(), EditorFormsComponent]
})
export class EditorFormsModule {}
