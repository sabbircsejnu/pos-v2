import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class KanbanLayoutService {
  // kanban app in edit sidebar open
  taskSlider = new Subject();
  storySlider = new Subject();

  taskSliderClosed = true;
  storySliderClosed = true;

  toggleTask() {
    this.taskSliderClosed = !this.taskSliderClosed;
    this.taskSlider.next(this.taskSliderClosed);
  }

  toggleStory() {
    this.storySliderClosed = !this.storySliderClosed;
    this.storySlider.next(this.storySliderClosed);
  }
}
