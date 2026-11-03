import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormControl, FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';

// third party import
import { Validators, Editor, Toolbar, NgxEditorModule } from 'ngx-editor';
import { QuillEditorComponent } from 'ngx-quill';

// project import
import { CardComponent } from 'src/app/@theme/components/card/card.component';
import jsonDoc from './editor';
@Component({
  selector: 'app-editor-forms',
  templateUrl: './editor-forms.component.html',
  styleUrls: ['./editor-forms.component.scss'],
  imports: [CardComponent, FormsModule, ReactiveFormsModule, NgxEditorModule, QuillEditorComponent]
})
export class EditorFormsComponent implements OnDestroy, OnInit {
  // public props
  editorDoc = jsonDoc;

  editor!: Editor;

  // life cycle event
  ngOnInit(): void {
    this.editor = new Editor();
  }

  ngOnDestroy(): void {
    this.editor.destroy();
  }

  // public method
  toolbar: Toolbar = [
    ['bold', 'italic'],
    ['underline', 'strike'],
    ['code', 'blockquote'],
    ['ordered_list', 'bullet_list'],
    [{ heading: ['h1', 'h2', 'h3', 'h4', 'h5', 'h6'] }],
    ['link', 'image'],
    ['text_color', 'background_color'],
    ['align_left', 'align_center', 'align_right', 'align_justify']
  ];

  form = new FormGroup({
    editorContent: new FormControl({ value: jsonDoc, disabled: false }, Validators.required())
  });
}
