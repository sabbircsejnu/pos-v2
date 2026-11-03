// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { UntypedFormControl, UntypedFormGroup } from '@angular/forms';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { FileUploadValidators, FileUploadModule } from '@iplab/ngx-file-upload';
import { UploaderModule } from 'angular-uploader';
import { Uploader, UploadWidgetConfig, UploadWidgetResult } from 'uploader';

const apiKey = 'free';

@Component({
  selector: 'app-drop-zone',
  imports: [CommonModule, SharedModule, FileUploadModule, UploaderModule],
  templateUrl: './drop-zone.component.html',
  styleUrls: ['./drop-zone.component.scss']
})
export class DropZoneComponent {
  // private Props
  private filesControl = new UntypedFormControl(null, FileUploadValidators.filesLimit(2));

  demoForm = new UntypedFormGroup({
    files: this.filesControl
  });

  // private method
  toggleStatus() {
    // eslint-disable-next-line
    this.filesControl.disabled ? this.filesControl.enable() : this.filesControl.disable();
  }

  uploader = Uploader({ apiKey });
  options: UploadWidgetConfig = {
    multi: true
  };
  onComplete = (files: UploadWidgetResult[]) => {
    this.uploadedFileUrl = files[0]?.fileUrl;
  };
  uploadedFileUrl: string | undefined = undefined;
}
