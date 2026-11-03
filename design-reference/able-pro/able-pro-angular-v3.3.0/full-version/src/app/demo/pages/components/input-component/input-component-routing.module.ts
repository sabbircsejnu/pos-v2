import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

const routes: Routes = [
  {
    path: '',
    children: [
      {
        path: 'button',
        loadComponent: () => import('./basic-button/basic-button.component').then((c) => c.BasicButtonComponent)
      },
      {
        path: 'button-toggle',
        loadComponent: () => import('./button-toggle/button-toggle.component').then((c) => c.ButtonToggleComponent)
      },
      {
        path: 'autocomplete',
        loadComponent: () => import('./auto-complete/auto-complete.component').then((c) => c.AutoCompleteComponent)
      },
      {
        path: 'form-field',
        loadComponent: () => import('./form-field/form-field.component').then((c) => c.FormFieldComponent)
      },
      {
        path: 'input-component',
        loadComponent: () => import('./input-compo/input-compo.component').then((c) => c.InputCompoComponent)
      },
      {
        path: 'select',
        loadComponent: () => import('./select/select.component').then((c) => c.SelectComponent)
      },
      {
        path: 'check-box',
        loadComponent: () => import('./check-box/check-box.component').then((c) => c.CheckBoxComponent)
      },
      {
        path: 'radio',
        loadComponent: () => import('./radio-button/radio-button.component').then((c) => c.RadioButtonComponent)
      },
      {
        path: 'spinner',
        loadComponent: () => import('./spinner/spinner.component').then((c) => c.SpinnerComponent)
      },
      {
        path: 'slider',
        loadComponent: () => import('./slider/slider.component').then((c) => c.SliderComponent)
      }
    ]
  }
];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class InputComponentRoutingModule {}
