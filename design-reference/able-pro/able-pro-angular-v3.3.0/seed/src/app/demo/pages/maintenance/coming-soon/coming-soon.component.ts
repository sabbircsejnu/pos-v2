// angular import
import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';

@Component({
  selector: 'app-coming-soon',
  imports: [CommonModule, SharedModule],
  templateUrl: './coming-soon.component.html',
  styleUrls: ['../maintenance.scss', './coming-soon.component.scss']
})
export class ComingSoonComponent implements OnInit {
  // public props
  hours = 0;
  minutes = 0;
  seconds = 0;
  daysLeft = 10;

  // life cycle event
  ngOnInit() {
    setInterval(() => {
      const currentDate = new Date();
      const hours = currentDate.getHours();
      const minutes = currentDate.getMinutes();
      const seconds = currentDate.getSeconds();
      this.hours = hours;
      this.minutes = minutes;
      this.seconds = seconds;

      // Update the daysLeft property based on the current date and a target date
      const targetDate = new Date('2024-12-31');
      const diff = Math.abs(targetDate.getTime() - currentDate.getTime());
      const daysLeft = Math.ceil(diff / (1000 * 60 * 60 * 24));
      this.daysLeft = daysLeft;
    }, 1000);
  }
}
