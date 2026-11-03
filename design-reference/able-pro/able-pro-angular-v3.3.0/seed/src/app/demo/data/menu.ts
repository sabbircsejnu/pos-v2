import { Navigation } from 'src/app/@theme/types/navigation';
import { Role } from 'src/app/@theme/types/role';

export const menus: Navigation[] = [
  {
    id: 'navigation',
    title: 'Navigation',
    type: 'group',
    icon: 'icon-navigation',
    role: [Role.Admin, Role.User],
    children: [
      {
        id: 'sample-page',
        title: 'Sample Page',
        type: 'item',
        classes: 'nav-item',
        url: '/sample-page',
        icon: '#custom-notification-status'
      }
    ]
  },

  {
    id: 'pages',
    title: 'Pages',
    type: 'group',
    icon: 'icon-navigation',
    role: [Role.Admin, Role.User],
    children: [
      {
        id: 'maintenance',
        title: 'Maintenance',
        type: 'collapse',
        icon: '#custom-flag',
        role: [Role.Admin, Role.User],
        children: [
          {
            id: 'error',
            title: 'Error 404',
            type: 'item',
            url: '/maintenance/error-404',
            target: true,
            breadcrumbs: false
          },
          {
            id: 'error-500',
            title: 'Error 500',
            type: 'item',
            url: '/maintenance/error-500',
            target: true,
            breadcrumbs: false
          },
          {
            id: 'under-constructor',
            title: 'Under Construction',
            type: 'item',
            url: '/maintenance/under-constructor',
            target: true,
            breadcrumbs: false
          },
          {
            id: 'coming-soon',
            title: 'Coming Soon',
            type: 'item',
            url: '/maintenance/coming-soon',
            target: true,
            breadcrumbs: false
          }
        ]
      },
      {
        id: 'contact-us',
        title: 'Contact Us',
        type: 'item',
        classes: 'nav-item',
        url: '/contact-us',
        icon: '#custom-24-support',
        target: true,
        breadcrumbs: false
      }
    ]
  },
  {
    id: 'other',
    title: 'Other',
    type: 'group',
    icon: 'icon-navigation',
    role: [Role.Admin, Role.User],
    children: [
      {
        id: 'menu-levels',
        title: 'Menu levels',
        type: 'collapse',
        icon: '#custom-level',
        role: [Role.Admin, Role.User],
        children: [
          {
            id: 'level-2-1',
            title: 'Level 2.1',
            type: 'item',
            url: 'javascript:void()void()',
            external: true
          },
          {
            id: 'menu-level-2.2',
            title: 'Menu Level 2.2',
            type: 'collapse',
            classes: 'edge',
            role: [Role.Admin, Role.User],
            children: [
              {
                id: 'menu-level-3.1',
                title: 'Menu Level 3.1',
                type: 'item',
                url: 'javascript:void()',
                external: true
              },
              {
                id: 'menu-level-3.2',
                title: 'Menu Level 3.2',
                type: 'item',
                url: 'javascript:void()',
                external: true
              },
              {
                id: 'menu-level-3.3',
                title: 'Menu Level 3.3',
                type: 'collapse',
                classes: 'edge',
                role: [Role.Admin, Role.User],
                children: [
                  {
                    id: 'menu-level-4.1',
                    title: 'Menu Level 4.1',
                    type: 'item',
                    url: 'javascript:void()',
                    external: true
                  },
                  {
                    id: 'menu-level-4.2',
                    title: 'Menu Level 4.2',
                    type: 'item',
                    url: 'javascript:void()',
                    external: true
                  }
                ]
              }
            ]
          },
          {
            id: 'menu-level-2.3',
            title: 'Menu Level 2.3',
            type: 'collapse',
            classes: 'edge',
            role: [Role.Admin, Role.User],
            children: [
              {
                id: 'menu-level-3.1',
                title: 'Menu Level 3.1',
                type: 'item',
                url: 'javascript:void()',
                external: true
              },
              {
                id: 'menu-level-3.2',
                title: 'Menu Level 3.2',
                type: 'item',
                url: 'javascript:void()',
                external: true
              },
              {
                id: 'menu-level-3.3',
                title: 'Menu Level 3.3',
                type: 'collapse',
                classes: 'edge',
                role: [Role.Admin, Role.User],
                children: [
                  {
                    id: 'menu-level-4.1',
                    title: 'Menu Level 4.1',
                    type: 'item',
                    url: 'javascript:void()',
                    external: true
                  },
                  {
                    id: 'menu-level-4.2',
                    title: 'Menu Level 4.2',
                    type: 'item',
                    url: 'javascript:void()',
                    external: true
                  }
                ]
              }
            ]
          }
        ]
      },
      {
        id: 'document',
        title: 'Document',
        type: 'item',
        classes: 'nav-item',
        url: 'https://phoenixcoded.gitbook.io/able-pro',
        icon: '#custom-gitBook',
        target: true,
        external: true
      }
    ]
  }
];
