// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-ac-personal',
  imports: [CommonModule, SharedModule],
  templateUrl: './ac-personal.component.html',
  styleUrls: ['../account-profile.scss', './ac-personal.component.scss']
})
export class AcPersonalComponent {
  // public props
  selectedExperience = '1';
  textareaValue =
    'Hello, I’m Anshan Handgun Creative Graphic Designer & User Experience Designer based in Website, I create digital Products a more Beautiful and usable place. Morbid accusant ipsum. Nam nec tellus at.';
}
