import { Route } from '@angular/router';
import { MenuItem, NAV_SECTIONS } from '../models/menu.model';
import { routes } from '../app.routes';

interface FlatRoute { path: string; permissions?: string[]; mode: 'any' | 'all'; guarded: boolean; }

function flatten(list: Route[], prefix = ''): FlatRoute[] {
  return list.flatMap(route => {
    const path = [prefix, route.path ?? ''].filter(Boolean).join('/');
    const own: FlatRoute[] = route.path === undefined && !route.children ? [] : [{
      path,
      permissions: route.data?.['permissions'] as string[] | undefined,
      mode: (route.data?.['permissionMode'] as 'any' | 'all' | undefined) ?? 'any',
      guarded: !!route.canActivate?.length
    }];
    return [...own, ...flatten(route.children ?? [], path)];
  });
}

function leaves(items: MenuItem[]): MenuItem[] {
  return items.flatMap(item => (item.children?.length ? leaves(item.children) : [item]));
}

const asList = (value?: string | string[]) => (value === undefined ? [] : Array.isArray(value) ? value : [value]);

describe('menu and route permission consistency', () => {
  const flat = flatten(routes);
  const menuLeaves = leaves(NAV_SECTIONS.flatMap(section => section.items)).filter(item => item.route);

  it('registers a route for every menu entry', () => {
    const missing = menuLeaves
      .filter(item => !flat.some(route => `/${route.path}` === item.route))
      .map(item => item.route);
    expect(missing).toEqual([]);
  });

  it('requires at least the permission the menu advertises, so a visible menu is never a dead link', () => {
    const mismatches = menuLeaves.flatMap(item => {
      const menuPermissions = asList(item.permission);
      const route = flat.find(candidate => `/${candidate.path}` === item.route);
      if (!route || menuPermissions.length === 0) return [];
      const routePermissions = route.permissions ?? [];
      const menuMode = item.permissionMode ?? 'any';
      // A user holding exactly the menu's permissions must also pass the route guard.
      const passes = routePermissions.length === 0
        || (route.mode === 'all'
          ? routePermissions.every(required => menuPermissions.includes(required) && menuMode === 'all')
          : routePermissions.some(required => menuPermissions.includes(required)));
      return passes ? [] : [`${item.route}: menu ${JSON.stringify(menuPermissions)}/${menuMode} vs route ${JSON.stringify(routePermissions)}/${route.mode}`];
    });
    expect(mismatches).toEqual([]);
  });

  it('opens the route for every individual permission an any-of menu entry advertises', () => {
    const dead = menuLeaves.flatMap(item => {
      const menuPermissions = asList(item.permission);
      const route = flat.find(candidate => `/${candidate.path}` === item.route);
      const routePermissions = route?.permissions ?? [];
      if (!route || menuPermissions.length === 0 || routePermissions.length === 0 || (item.permissionMode ?? 'any') === 'all') {
        return [];
      }
      return menuPermissions
        .filter(held => (route.mode === 'all'
          ? !routePermissions.every(required => required === held)
          : !routePermissions.includes(held)))
        .map(held => `${item.route}: menu shows for ${held} but the route requires ${JSON.stringify(routePermissions)}/${route.mode}`);
    });
    expect(dead).toEqual([]);
  });

  it('protects every permission-gated menu route with a guard', () => {
    const unguarded = menuLeaves
      .filter(item => item.permission)
      .filter(item => !flat.find(route => `/${route.path}` === item.route)?.guarded)
      .map(item => item.route);
    expect(unguarded).toEqual([]);
  });
});
