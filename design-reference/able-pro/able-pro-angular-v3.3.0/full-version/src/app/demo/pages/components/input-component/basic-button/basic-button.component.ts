// Angular Import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

//  project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-basic-button',
  imports: [CommonModule, SharedModule],
  templateUrl: './basic-button.component.html',
  styleUrls: ['./basic-button.component.scss']
})
export class BasicButtonComponent {}
