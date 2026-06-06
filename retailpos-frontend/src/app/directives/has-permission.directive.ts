import { Directive, ElementRef, Input, OnDestroy, OnInit, Renderer2 } from '@angular/core';
import { Subscription } from 'rxjs';
import { AuthService } from '../services/auth.service';

@Directive({
  selector: '[appHasPermission]',
  standalone: true
})
export class HasPermissionDirective implements OnInit, OnDestroy {
  @Input('appHasPermission') permission!: string | string[];
  @Input() appHasPermissionMode: 'any' | 'all' = 'any';

  private authSubscription?: Subscription;

  constructor(
    private authService: AuthService,
    private elementRef: ElementRef,
    private renderer: Renderer2
  ) {}

  ngOnInit(): void {
    this.authSubscription = this.authService.currentUser$.subscribe(() => {
      this.updateVisibility();
    });
    this.updateVisibility();
  }

  ngOnDestroy(): void {
    this.authSubscription?.unsubscribe();
  }

  private updateVisibility(): void {
    const hasAccess = this.checkAccess();
    this.renderer.setStyle(this.elementRef.nativeElement, 'display', hasAccess ? '' : 'none');
    this.renderer.setAttribute(this.elementRef.nativeElement, 'aria-hidden', hasAccess ? 'false' : 'true');
  }

  private checkAccess(): boolean {
    if (!this.permission) {
      return true;
    }

    if (Array.isArray(this.permission)) {
      if (this.permission.length === 0) {
        return true;
      }

      return this.appHasPermissionMode === 'all'
        ? this.authService.hasAllPermissions(this.permission)
        : this.authService.hasAnyPermission(this.permission);
    }

    return this.authService.hasPermission(this.permission);
  }
}
