import { CommonModule } from '@angular/common';
import { Component, EventEmitter, HostListener, Input, Output, signal } from '@angular/core';

@Component({
  selector: 'app-image-lightbox',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="fixed inset-0 z-50 bg-black/80 flex items-center justify-center" (click)="close()">
      <div class="absolute top-4 right-4 flex gap-2">
        <a *ngIf="originalUrl" [href]="originalUrl" target="_blank" rel="noopener"
           class="px-3 py-1 text-sm text-white bg-white/10 rounded hover:bg-white/20"
           (click)="$event.stopPropagation()">
          View original{{ width && height ? ' (' + width + '×' + height + ')' : '' }}
        </a>
        <button class="px-3 py-1 text-white bg-white/10 rounded hover:bg-white/20" (click)="close()">×</button>
      </div>
      <img [src]="src" (click)="$event.stopPropagation()"
           class="max-w-[92vw] max-h-[90vh] rounded shadow-2xl object-contain bg-white" />
    </div>
  `,
})
export class ImageLightboxComponent {
  @Input({ required: true }) src!: string;
  @Input() originalUrl?: string;
  @Input() width?: number;
  @Input() height?: number;
  @Output() closed = new EventEmitter<void>();

  close() { this.closed.emit(); }

  @HostListener('document:keydown.escape')
  onEsc() { this.close(); }
}
