import { Navigation } from 'src/app/@theme/types/navigation';

export const componentMenus: Navigation[] = [
  {
    id: 'input',
    title: 'Inputs',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'button',
        title: 'Button',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/button/overview',
        url: '/components/input/button'
      },
      {
        id: 'button-toggle',
        title: 'Button Toggle',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/button-toggle/overview',
        url: '/components/input/button-toggle'
      },
      {
        id: 'auto-complete',
        title: 'Autocomplete',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/autocomplete/overview',
        url: '/components/input/autocomplete'
      },
      {
        id: 'input',
        title: 'Input',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/input/overview',
        url: '/components/input/input-component'
      },
      {
        id: 'form-field',
        title: 'Form Field',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/form-field/overview',
        url: '/components/input/form-field'
      },
      {
        id: 'select',
        title: 'Select',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/select/overview',
        url: '/components/input/select'
      },
      {
        id: 'check-box',
        title: 'Check Box',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/checkbox/overview',
        url: '/components/input/check-box'
      },
      {
        id: 'radio',
        title: 'Radio',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/radio/overview',
        url: '/components/input/radio'
      },
      {
        id: 'spinner',
        title: 'Spinner',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/progress-spinner/overview',
        url: '/components/input/spinner'
      },
      {
        id: 'slider',
        title: 'Slider',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/slider/overview',
        url: '/components/input/slider'
      }
    ]
  },

  {
    id: 'data-display',
    title: 'Data Display',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'badge',
        title: 'Badge',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/badge/overview',
        url: '/components/d-display/badge'
      },
      {
        id: 'list',
        title: 'List',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/list/overview',
        url: '/components/d-display/list'
      },
      {
        id: 'chips',
        title: 'Chips',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/chips/overview',
        url: '/components/d-display/chips'
      },
      {
        id: 'tooltip',
        title: 'Tooltip',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/tooltip/overview',
        url: '/components/d-display/tooltip'
      },

      {
        id: 'typography',
        title: 'Typography',
        type: 'item',
        classes: 'nav-item',
        link: 'javascript:void();',
        url: '/components/d-display/typography'
      }
    ]
  },

  {
    id: 'feed-back',
    title: 'FeedBack',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'dialog',
        title: 'Dialog',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/dialog/overview',
        url: '/components/feedback/dialog'
      },
      {
        id: 'progress-bar',
        title: 'Progress Bar',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/progress-bar/overview',
        url: '/components/feedback/progress-bar'
      },
      {
        id: 'snackbar',
        title: 'Snackbar',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/snack-bar/overview',
        url: '/components/feedback/snackbar'
      }
    ]
  },

  {
    id: 'navigation',
    title: 'Navigation',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'paginator',
        title: 'Paginator',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/paginator/overview',
        url: '/components/navigation/paginator'
      },
      {
        id: 'tabs',
        title: 'Tabs',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/tabs/overview',
        url: '/components/navigation/tabs'
      },
      {
        id: 'stepper',
        title: 'Stepper',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/stepper/overview',
        url: '/components/navigation/stepper'
      }
    ]
  },
  {
    id: 'surface',
    title: 'Surfaces',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'accordion',
        title: 'Accordion',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/cdk/accordion/examples',
        url: '/components/surface/accordion'
      },
      {
        id: 'cards',
        title: 'Cards',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/card/overview',
        url: '/components/surface/card'
      }
    ]
  },
  {
    id: 'utils',
    title: 'Utils',
    type: 'group',
    icon: 'icon-navigation',
    children: [
      {
        id: 'color',
        title: 'Colors',
        type: 'item',
        classes: 'nav-item',
        link: 'javascript:void()',
        url: '/components/utils/color'
      },
      {
        id: 'date',
        title: 'Date Picker',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/cdk/datepicker/examples',
        url: '/components/utils/date-picker'
      },
      {
        id: 'tree-view',
        title: 'Tree View',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/components/tree/overview',
        url: '/components/utils/tree'
      },
      {
        id: 'drag-drop',
        title: 'Drag Drop',
        type: 'item',
        classes: 'nav-item',
        link: 'https://material.angular.io/cdk/drag-drop/overview',
        url: '/components/utils/drag-drop'
      }
    ]
  }
];
