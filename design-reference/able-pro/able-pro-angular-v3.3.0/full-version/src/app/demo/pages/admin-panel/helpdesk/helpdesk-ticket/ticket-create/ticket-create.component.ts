// angular import
import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { QuillModule } from 'ngx-quill';
import { FileUploadControl, FileUploadModule, FileUploadValidators } from '@iplab/ngx-file-upload';

// rxjs
import { BehaviorSubject, Subscription } from 'rxjs';

@Component({
  selector: 'app-ticket-create',
  imports: [SharedModule, QuillModule, FileUploadModule, CommonModule],
  templateUrl: './ticket-create.component.html',
  styleUrl: './ticket-create.component.scss'
})
export class TicketCreateComponent implements OnDestroy, OnInit {
  // public props
  fileSub = new Subscription();

  // private props
  // eslint-disable-next-line
  readonly uploadedFile: BehaviorSubject<any> = new BehaviorSubject(null);
  readonly control = new FileUploadControl({ listVisible: true, accept: ['image/*'], discardInvalid: true, multiple: false }, [
    FileUploadValidators.accept(['image/*']),
    FileUploadValidators.filesLimit(1)
  ]);

  // life cycle
  ngOnInit(): void {
    this.fileSub = this.control.valueChanges.subscribe((values: Array<File>) => this.getImage(values[0]));
  }

  ngOnDestroy(): void {
    this.fileSub.unsubscribe();
  }

  // private method
  private getImage(file: File): void {
    if (FileReader && file) {
      const fr = new FileReader();
      fr.onload = (e) => this.uploadedFile.next(e.target!.result);
      fr.readAsDataURL(file);
    } else {
      this.uploadedFile.next(null);
    }
  }
}
