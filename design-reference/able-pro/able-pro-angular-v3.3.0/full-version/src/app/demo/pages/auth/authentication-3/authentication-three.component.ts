// angular import
import { Component, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { FormBuilder, FormControl, Validators } from '@angular/forms';

// third party
import { CarouselModule, OwlOptions } from 'ngx-owl-carousel-o';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

@Component({
  selector: 'app-authentication-three',
  imports: [CommonModule, SharedModule, RouterModule, CarouselModule],
  templateUrl: './authentication-three.component.html',
  styleUrls: ['./authentication-three.component.scss', '../authentication.scss']
})
export class AuthenticationThreeComponent {
  private _formBuilder = inject(FormBuilder);
  private themeService = inject(ThemeLayoutService);

  // public props
  showStepper = false;
  hide = true;
  coHide = true;
  isDark: boolean = false;

  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // private methods
  private isDarkTheme(isDark: string) {
    this.isDark = isDark === 'dark' ? true : false;
  }

  customOptions: OwlOptions = {
    loop: true,
    autoplayHoverPause: true,
    mouseDrag: false,
    touchDrag: false,
    pullDrag: false,
    dots: true,
    items: 1,
    margin: 30,
    navSpeed: 700,
    nav: false,
    rtl: true,
    navText: ['', '']
  };
  email = new FormControl('', [Validators.required, Validators.email]);
  secondFormGroup = this._formBuilder.group({
    secondCtrl: ['', Validators.required]
  });

  // public method
  getErrorMessage() {
    if (this.email.hasError('required')) {
      return '';
    }
    return this.email.hasError('email') ? 'Not a valid email' : '';
  }
}
