// angular import
import { Component, effect, inject } from '@angular/core';
import { CommonModule } from '@angular/common';

// project import
import { SharedModule } from 'src/app/demo/shared/shared.module';
import { ThemeLayoutService } from 'src/app/@theme/services/theme-layout.service';

@Component({
  selector: 'app-colors-component',
  imports: [CommonModule, SharedModule],
  templateUrl: './colors-component.component.html',
  styleUrls: ['./colors-component.component.scss']
})
export class ColorsComponentComponent {
  themeService = inject(ThemeLayoutService);

  DarkMode: boolean = false;

  // constructor
  constructor() {
    effect(() => {
      this.isDarkTheme(this.themeService.isDarkMode());
    });
  }

  // private method
  private isDarkTheme(isDark: string) {
    this.DarkMode = isDark === 'dark' ? true : false;
  }

  // public method
  colors = [
    {
      colorName: 'Primary Color',
      colorPlatte: [
        {
          name: 'Primary.lighter',
          label: 'Blue-1',
          light: '#E9F0FF',
          dark: '#DAE6FF',
          class: 'bg-primary-50',
          classDark: 'text-accent-50',
          classLight: 'text-accent-800'
        },
        { name: 'Primary[100]', label: 'Blue-2', light: '#DAE6FF', dark: '#176CD6', class: 'bg-primary-100' },
        { name: 'Primary[200]', label: 'Blue-3', light: '#B5CCFF', dark: '#90B3FF', class: 'bg-primary-200' },
        { name: 'Primary.light', label: 'Blue-4', light: '#90B3FF', dark: '#90B3FF', class: 'bg-primary-300' },
        { name: 'Primary[400]', label: 'Blue-5', light: '#6B99FF', dark: '#2281DF', class: 'bg-primary-400 text-white' },
        { name: 'Primary.main', label: 'Blue-6', light: '#4680FF', dark: '#4680FF', class: 'bg-primary-500 text-white block-border' },
        { name: 'Primary.dark', label: 'Blue-7', light: '#2281DF', dark: '#6B99FF', class: 'bg-primary-600 text-white' },
        { name: 'Primary[700]', label: 'Blue-8', light: '#1C76DA', dark: '#90B3FF', class: 'bg-primary-700 text-accent-50' },
        { name: 'Primary.darker', label: 'Blue-9', light: '#176CD6', dark: '#B5CCFF', class: 'bg-primary-800 text-accent-50' },
        { name: 'Primary[900]', label: 'Blue-10', light: '#0D59CF', dark: '#DAE6FF', class: 'bg-primary-900 text-accent-50' }
      ]
    },
    {
      colorName: 'Secondary Color',
      colorPlatte: [
        {
          name: 'secondary.lighter',
          label: 'Grey-1',
          light: '#F8F9FA',
          dark: '#1D2630',
          class: 'bg-accent-100'
        },
        { name: 'secondary[100]', label: 'Grey-2', dark: '#131920', light: '#F8F9FA', class: 'bg-accent-100' },
        { name: 'secondary[200]', label: 'Grey-3', dark: '#1D2630', light: '#90B3FF', class: 'bg-accent-200' },
        { name: 'secondary.light', label: 'Grey-4', dark: '#3E4853', light: '#DBE0E5', class: 'bg-accent-300' },
        { name: 'secondary[400]', label: 'Grey-5', dark: '#5B6B79', light: '#BEC8D0', class: 'bg-accent-400 text-white' },
        { name: 'secondary.main', label: 'Grey-6', dark: '#8996A4', light: '#8996A4', class: 'bg-accent-500 text-white block-border' },
        { name: 'secondary.dark', label: 'Grey-7', dark: '#BEC8D0', light: '#5B6B79', class: 'bg-accent-600 text-accent-50' },
        { name: 'secondary[700]', label: 'Grey-8', dark: '#DBE0E5', light: '#3E4853', class: 'bg-accent-700 text-accent-50' },
        { name: 'secondary.darker', label: 'Grey-9', dark: '#F3F5F7', light: '#1D2630', class: 'bg-accent-800 text-accent-50' },
        { name: 'secondary[900]', label: 'Grey-10', dark: '#F8F9FA', light: '#131920', class: 'bg-accent-900 text-accent-50' }
      ]
    }
  ];

  subColor = [
    {
      colorName: 'Success Color',
      colorPlatte: [
        {
          lightName: 'success.lighter',
          darkName: 'success.darker',
          label: 'Green-1',
          light: '#C0E5D9',
          dark: '#107D4F',
          class: 'bg-success-100'
        },
        {
          lightName: 'success.light',
          darkName: 'success.dark',
          label: 'Green-4',
          light: '#6BC2A5',
          dark: '#21976C',
          class: 'bg-success-300 '
        },
        {
          lightName: 'success.main',
          darkName: 'success.main',
          label: 'Green-6',
          light: '#2CA87F',
          dark: '#2CA87F',
          class: 'bg-success-500 text-white block-border'
        },
        {
          lightName: 'success.dark',
          darkName: 'success.light',
          label: 'Green-8',
          light: '#21976C',
          dark: '#6BC2A5',
          class: 'bg-success-700 text-accent-50'
        },
        {
          lightName: 'success.darker',
          darkName: 'success.lighter',
          label: 'Green-10',
          light: '#107D4F',
          dark: '#C0E5D9',
          class: 'bg-success-900 text-accent-50'
        }
      ]
    },
    {
      colorName: 'Error Color',
      colorPlatte: [
        {
          lightName: 'error.lighter',
          darkName: 'error.darker',
          label: 'Red-1',
          light: '#F5BEBE',
          dark: '#C50D0D',
          class: 'bg-warn-100'
        },
        {
          lightName: 'error.light',
          darkName: 'error.dark',
          label: 'Red-4',
          light: '#E76767',
          dark: '#D31C1C',
          class: 'bg-warn-300 '
        },
        {
          lightName: 'error.main',
          darkName: 'error.main',
          label: 'Red-6',
          light: '#DC2626',
          dark: '#DC2626',
          class: 'bg-warn-500 text-white block-border'
        },
        {
          lightName: 'error.dark',
          darkName: 'error.light',
          label: 'Red-8',
          light: '#D31C1C',
          dark: '#E76767',
          class: 'bg-warn-700 text-accent-50'
        },
        {
          lightName: 'error.darker',
          darkName: 'error.lighter',
          label: 'Red-10',
          light: '#C50D0D',
          dark: '#F5BEBE',
          class: 'bg-warn-900 text-accent-50'
        }
      ]
    },
    {
      colorName: 'Warning Color',
      colorPlatte: [
        {
          lightName: 'warning.lighter',
          darkName: 'warning.darker',
          label: 'Gold-1',
          light: '#F7DCB3',
          dark: '#D35A00',
          class: 'bg-warning-100'
        },
        {
          lightName: 'warning.light',
          darkName: 'warning.dark',
          label: 'Gold-4',
          light: '#EDAD4D',
          dark: '#DE7700',
          class: 'bg-warning-300 '
        },
        {
          lightName: 'warning.main',
          darkName: 'warning.main',
          label: 'Gold-6',
          light: '#E58A00',
          dark: '#E58A00',
          class: 'bg-warning-500 text-white block-border'
        },
        {
          lightName: 'warning.dark',
          darkName: 'warning.light',
          label: 'Gold-8',
          light: '#DE7700',
          dark: '#EDAD4D',
          class: 'bg-warning-700 text-accent-50'
        },
        {
          lightName: 'warning.darker',
          darkName: 'warning.lighter',
          label: 'Gold-10',
          light: '#D35A00',
          dark: '#F7DCB3',
          class: 'bg-warning-900 text-accent-50'
        }
      ]
    }
  ];
}
