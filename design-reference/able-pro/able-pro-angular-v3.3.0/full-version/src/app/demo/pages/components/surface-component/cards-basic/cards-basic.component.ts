// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-cards-basic',
  imports: [CommonModule, SharedModule],
  templateUrl: './cards-basic.component.html',
  styleUrls: ['./cards-basic.component.scss']
})
export class CardsBasicComponent {
  // public prop
  longText = `The Shiba Inu is the smallest of the six original and distinct spitz breeds of dog
  from Japan. A small, agile dog that copes very well with mountainous terrain, the Shiba Inu was
  originally bred for hunting.`;
}
