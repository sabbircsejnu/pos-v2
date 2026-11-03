// angular imports
import { Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { FileUploadControl, FileUploadModule, FileUploadValidators } from '@iplab/ngx-file-upload';

// rxjs
import { BehaviorSubject, Subscription } from 'rxjs';

@Component({
  selector: 'app-courses-add',
  imports: [SharedModule, FileUploadModule, CommonModule],
  templateUrl: './courses-add.component.html',
  styleUrl: './courses-add.component.scss'
})
export class CoursesAddComponent implements OnInit, OnDestroy {
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
