// angular import
import { Component, inject } from '@angular/core';
import { FormBuilder, FormControl, FormsModule } from '@angular/forms';

// Angular Material
import { PageEvent, MatPaginator } from '@angular/material/paginator';
import { FloatLabelType, MatFormField } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatSlideToggle } from '@angular/material/slide-toggle';

// project import
import { CardComponent } from 'src/app/@theme/components/card/card.component';

@Component({
  selector: 'app-paginator',
  templateUrl: './paginator.component.html',
  styleUrls: ['./paginator.component.scss'],
  imports: [CardComponent, MatFormField, MatInput, FormsModule, MatSlideToggle, MatPaginator]
})
export class PaginatorComponent {
  private _formBuilder = inject(FormBuilder);

  // public props
  length = 50;
  pageSize = 10;
  pageIndex = 0;
  pageSizeOptions = [5, 10, 25];
  hidePageSize = false;
  showPageSizeOptions = true;
  showFirstLastButtons = true;
  disabled = false;
  pageEvent: PageEvent;

  // public method
  handlePageEvent(e: PageEvent) {
    this.pageEvent = e;
    this.length = e.length;
    this.pageSize = e.pageSize;
    this.pageIndex = e.pageIndex;
  }

  setPageSizeOptions(setPageSizeOptionsInput: string) {
    if (setPageSizeOptionsInput) {
      this.pageSizeOptions = setPageSizeOptionsInput.split(',').map((str) => +str);
    }
  }

  getFloatLabelValue(): FloatLabelType {
    return this.floatLabelControl.value || 'always';
  }

  floatLabelControl = new FormControl('always' as FloatLabelType);
  options = this._formBuilder.group({
    floatLabel: this.floatLabelControl
  });
}
