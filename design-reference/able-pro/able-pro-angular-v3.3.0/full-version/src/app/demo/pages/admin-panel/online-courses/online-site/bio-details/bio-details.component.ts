// angular import
import { Component } from '@angular/core';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

// third party
import { QuillModule } from 'ngx-quill';

@Component({
  selector: 'app-bio-details',
  imports: [SharedModule, QuillModule],
  templateUrl: './bio-details.component.html',
  styleUrl: './bio-details.component.scss'
})
export class BioDetailsComponent {}
