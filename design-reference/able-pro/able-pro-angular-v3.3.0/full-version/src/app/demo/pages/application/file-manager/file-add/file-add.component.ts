// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { FileUploadModule, FileUploadValidators } from '@iplab/ngx-file-upload';
import { UntypedFormControl, UntypedFormGroup } from '@angular/forms';

@Component({
  selector: 'app-file-add',
  imports: [CommonModule, SharedModule, FileUploadModule],
  templateUrl: './file-add.component.html',
  styleUrls: ['./file-add.component.scss']
})
export class FileAddComponent {
  // public props
  private filesControl = new UntypedFormControl(null, FileUploadValidators.filesLimit(10));
  demoForm = new UntypedFormGroup({
    files: this.filesControl
  });
}
