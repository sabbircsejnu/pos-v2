// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-basic-forms',
  imports: [CommonModule, SharedModule],
  templateUrl: './basic-forms.component.html',
  styleUrls: ['./basic-forms.component.scss']
})
export class BasicFormsComponent {}
