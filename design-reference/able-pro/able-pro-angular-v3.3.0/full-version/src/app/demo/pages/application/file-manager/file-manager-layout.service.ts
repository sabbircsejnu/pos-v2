import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class FileManagerLayoutService {
  // file manager
  fileSlider = new Subject();
  fileSliderOpen = true;

  toggleFileSide() {
    this.fileSlider.next(!this.fileSliderOpen);
  }
}
