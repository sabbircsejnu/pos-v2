// angular import
import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { QuillModule } from 'ngx-quill';

@Component({
  selector: 'app-compose-mail',
  imports: [CommonModule, SharedModule, QuillModule],
  templateUrl: './compose-mail.component.html',
  styleUrls: ['./compose-mail.component.scss']
})
export class ComposeMailComponent {
  newMailEnter = false;

  onClick() {
    this.newMailEnter = !this.newMailEnter;
  }
}
