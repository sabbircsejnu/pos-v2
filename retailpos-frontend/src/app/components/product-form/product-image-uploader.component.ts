import { CommonModule } from '@angular/common';
import {
  Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject, signal,
} from '@angular/core';
import { ProductImage, PRODUCT_IMAGE_RULES } from '../../models/product-image.model';
import { ProductImageService } from '../../services/product-image.service';
import { ImageLightboxComponent } from './image-lightbox.component';
import { UiAlertComponent } from '../shared/ui/ui-alert.component';

interface UploadFailure { name: string; message: string; }
interface PendingFile { file: File; previewUrl: string; }

@Component({
  selector: 'app-product-image-uploader',
  standalone: true,
  imports: [CommonModule, ImageLightboxComponent, UiAlertComponent],
  template: `
    <div class="space-y-3">

      <!-- Image changes need products.edit (enforced by the API); without it the media is view-only. -->
      <app-ui-alert *ngIf="!canEdit" variant="info">
        Adding, replacing or removing product images requires the Edit Products permission.
      </app-ui-alert>

      <!-- Drop zone: pending queue in create mode, direct upload in edit mode -->
      <label class="pu-drop" *ngIf="canEdit">
        <span class="pu-drop__icon" aria-hidden="true"><i class="fas fa-cloud-upload-alt"></i></span>
        <span class="pu-drop__title">
          <strong>Click to upload</strong> or drop images here
        </span>
        <span class="pu-drop__rules">
          JPG / PNG / WEBP, up to {{ rules.maxBytes / (1024*1024) }} MB. Large images are optimized automatically.
        </span>
        <input type="file" class="sr-only" multiple
               aria-label="Upload product images"
               [accept]="rules.allowedMimes.join(',')"
               (change)="onFiles($any($event.target).files)" />
      </label>

      <!-- Validation failures -->
      <div *ngIf="failures().length" class="space-y-1">
        <app-ui-alert *ngFor="let f of failures()" variant="danger"><strong>{{ f.name }}:</strong> {{ f.message }}</app-ui-alert>
      </div>

      <!-- Upload progress (edit mode) -->
      <div *ngIf="uploading().size" class="pu-progress" role="status">
        <span class="pu-spinner" aria-hidden="true"></span>
        Uploading {{ uploading().size }} file(s)…
      </div>

      <!-- Pending notice (create mode) -->
      <app-ui-alert *ngIf="!productId && pendingFiles().length" variant="warning">
        {{ pendingFiles().length }} image(s) queued — will be uploaded when you save the product.
      </app-ui-alert>

      <!-- Image grid: uploaded (edit) + pending (create) -->
      <ul class="pu-grid" *ngIf="images().length || pendingFiles().length">

        <!-- Uploaded images (edit mode) -->
        <li *ngFor="let img of images()" class="pu-tile">
          <img [src]="svc.resolveUrl(img.thumbUrl)" alt="Product image" loading="lazy" decoding="async"
               (error)="onImgError($event)" />

          <span *ngIf="img.isPrimary" class="pu-flag pu-flag--primary">Primary</span>

          <div class="pu-tools">
            <button type="button" title="View" aria-label="View image" (click)="open(img)">
              <i class="fas fa-eye" aria-hidden="true"></i>
            </button>
            <button type="button" title="Set primary" aria-label="Set as primary image" *ngIf="canEdit && !img.isPrimary" (click)="setPrimary(img)">
              <i class="fas fa-star" aria-hidden="true"></i>
            </button>
            <button type="button" title="Delete" aria-label="Delete image" class="pu-tools__danger" *ngIf="canEdit" (click)="remove(img)">
              <i class="fas fa-times" aria-hidden="true"></i>
            </button>
          </div>
        </li>

        <!-- Pending images (create mode — queued, not yet uploaded) -->
        <li *ngFor="let p of pendingFiles(); let i = index" class="pu-tile pu-tile--pending">
          <img [src]="p.previewUrl" [alt]="'Queued image ' + p.file.name" />

          <span class="pu-flag pu-flag--queued">Queued</span>

          <div class="pu-tools">
            <button type="button" title="Remove" [attr.aria-label]="'Remove queued image ' + p.file.name" class="pu-tools__danger" (click)="removePending(i)">
              <i class="fas fa-times" aria-hidden="true"></i>
            </button>
          </div>
        </li>

      </ul>
    </div>

    <app-image-lightbox *ngIf="lightboxImage()"
      [src]="svc.resolveUrl(lightboxImage()!.mediumUrl)"
      [originalUrl]="svc.resolveUrl(lightboxImage()!.originalUrl)"
      [width]="lightboxImage()!.width"
      [height]="lightboxImage()!.height"
      (closed)="lightboxImage.set(null)"></app-image-lightbox>
  `,
  styles: [`
    :host {
      display: block;
      font-family: var(--rp-font-family-base);
    }

    .pu-drop {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--rp-space-1);
      width: 100%;
      min-height: 7rem;
      padding: var(--rp-space-3);
      border: 2px dashed var(--rp-color-border-control);
      border-radius: var(--rp-radius-card);
      background: var(--rp-color-surface-card);
      color: var(--rp-color-text-primary);
      font-size: var(--rp-text-body-size);
      text-align: center;
      cursor: pointer;
      transition: background-color 0.15s;
    }

    .pu-drop:hover {
      background: var(--rp-color-surface-muted);
    }

    .pu-drop:focus-within {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .pu-drop__icon {
      color: var(--rp-color-primary-fg);
      font-size: 1.25rem;
    }

    .pu-drop__rules {
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
    }

    .pu-progress {
      display: flex;
      align-items: center;
      gap: var(--rp-space-2);
      color: var(--rp-color-text-secondary);
      font-size: var(--rp-text-helper-size);
    }

    .pu-spinner {
      width: 0.75rem;
      height: 0.75rem;
      border: 2px solid var(--rp-color-border-control);
      border-top-color: var(--rp-color-action);
      border-radius: 9999px;
      animation: pu-spin 0.7s linear infinite;
    }

    @keyframes pu-spin {
      to { transform: rotate(360deg); }
    }

    .pu-grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--rp-space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    @media (min-width: 640px) {
      .pu-grid { grid-template-columns: repeat(3, minmax(0, 1fr)); }
    }

    @media (min-width: 1280px) {
      .pu-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }

    .pu-tile {
      position: relative;
      overflow: hidden;
      border: 1px solid var(--rp-color-border);
      border-radius: var(--rp-radius-md);
      background: var(--rp-color-surface-card);
    }

    .pu-tile--pending {
      border-color: var(--rp-color-status-warning-border);
      background: var(--rp-color-status-warning-bg);
    }

    .pu-tile img {
      display: block;
      width: 100%;
      height: 7rem;
      object-fit: contain;
      background: var(--rp-color-surface-muted);
    }

    .pu-flag {
      position: absolute;
      top: var(--rp-space-1);
      left: var(--rp-space-1);
      padding: 0 var(--rp-space-2);
      border-radius: var(--rp-radius-sm);
      background: var(--rp-color-action);
      color: var(--rp-color-on-action);
      font-size: 0.6875rem;
      font-weight: var(--rp-font-weight-semibold);
    }

    .pu-flag--queued {
      background: var(--rp-color-status-warning-fg);
    }

    /* Actions show on hover and keyboard focus, and always on touch devices that cannot hover. */
    .pu-tools {
      position: absolute;
      inset: 0;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--rp-space-2);
      background: var(--rp-color-backdrop);
      opacity: 0;
      transition: opacity 0.15s;
    }

    .pu-tile:hover .pu-tools,
    .pu-tile:focus-within .pu-tools {
      opacity: 1;
    }

    @media (hover: none) {
      .pu-tools {
        inset: auto 0 0 0;
        padding: var(--rp-space-1);
        opacity: 1;
      }
    }

    .pu-tools button {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border: 0;
      border-radius: var(--rp-radius-md);
      background: var(--rp-color-surface-card);
      color: var(--rp-color-text-primary);
      cursor: pointer;
    }

    .pu-tools button:hover {
      background: var(--rp-color-surface-hover);
    }

    .pu-tools button:focus-visible {
      outline: var(--rp-focus-ring-width) solid var(--rp-color-focus-ring);
      outline-offset: var(--rp-focus-ring-offset);
    }

    .pu-tools .pu-tools__danger {
      color: var(--rp-color-status-danger-fg);
    }
  `],
})
export class ProductImageUploaderComponent implements OnChanges, OnDestroy {
  svc = inject(ProductImageService);

  @Input() productId: number | null = null;
  /** Upload, set-primary and delete require products.edit; the server enforces it, the UI only avoids a 403. */
  @Input() canEdit = true;
  @Output() primaryChanged = new EventEmitter<ProductImage | null>();

  rules = PRODUCT_IMAGE_RULES;
  images = signal<ProductImage[]>([]);
  uploading = signal<Set<string>>(new Set());
  failures = signal<UploadFailure[]>([]);
  lightboxImage = signal<ProductImage | null>(null);
  pendingFiles = signal<PendingFile[]>([]);

  ngOnChanges(c: SimpleChanges): void {
    if (c['productId'] && this.productId) this.reload();
  }

  ngOnDestroy(): void {
    this.pendingFiles().forEach(p => URL.revokeObjectURL(p.previewUrl));
  }

  reload() {
    if (!this.productId) return;
    this.svc.list(this.productId).subscribe((imgs) => {
      this.images.set(imgs);
      this.primaryChanged.emit(imgs.find((i) => i.isPrimary) ?? null);
    });
  }

  async onFiles(files: FileList | null) {
    if (!files || !this.canEdit) return;
    this.failures.set([]);
    for (let i = 0; i < files.length; i++) {
      const f = files.item(i);
      if (!f) continue;
      const err = await this.preValidate(f);
      if (err) {
        this.failures.update((x) => [...x, { name: f.name, message: err }]);
        continue;
      }
      if (!this.productId) {
        // Create mode: queue file with a local preview URL
        const previewUrl = URL.createObjectURL(f);
        this.pendingFiles.update(arr => [...arr, { file: f, previewUrl }]);
      } else {
        this.uploadOne(f);
      }
    }
  }

  /** Returns queued files that have not yet been uploaded (create mode). */
  getPendingFiles(): File[] {
    return this.pendingFiles().map(p => p.file);
  }

  /** Removes one queued file by index. */
  removePending(index: number): void {
    this.pendingFiles.update(arr => {
      URL.revokeObjectURL(arr[index].previewUrl);
      return arr.filter((_, i) => i !== index);
    });
  }

  /** Clears all queued files and revokes their blob URLs. */
  clearPendingFiles(): void {
    this.pendingFiles().forEach(p => URL.revokeObjectURL(p.previewUrl));
    this.pendingFiles.set([]);
  }

  private async preValidate(f: File): Promise<string | null> {
    if (!this.rules.allowedMimes.includes(f.type)) return 'Only JPG, PNG, or WEBP allowed.';
    if (f.size > this.rules.maxBytes) return `File too large (max ${this.rules.maxBytes / (1024 * 1024)} MB).`;
    const dim = await this.readDimensions(f);
    if (!dim) return 'The file is not a valid JPG, PNG, or WEBP image, or it is corrupted.';
    if (dim.w < this.rules.minDim || dim.h < this.rules.minDim) {
      return `Image is too small: each side must be at least ${this.rules.minDim} px. Got ${dim.w}×${dim.h}.`;
    }
    if (dim.w * dim.h > this.rules.maxPixels) {
      return `Image is too large to process: the limit is ${this.rules.maxPixels / 1_000_000} megapixels. Got ${dim.w}×${dim.h}.`;
    }
    return null;
  }

  private readDimensions(f: File): Promise<{ w: number; h: number } | null> {
    return new Promise((resolve) => {
      const url = URL.createObjectURL(f);
      const img = new Image();
      img.onload = () => { URL.revokeObjectURL(url); resolve({ w: img.naturalWidth, h: img.naturalHeight }); };
      img.onerror = () => { URL.revokeObjectURL(url); resolve(null); };
      img.src = url;
    });
  }

  private uploadOne(f: File) {
    if (!this.productId) return;
    this.uploading.update((s) => { const next = new Set(s); next.add(f.name); return next; });
    this.svc.upload(this.productId, f).subscribe({
      next: () => {
        this.uploading.update((s) => { const next = new Set(s); next.delete(f.name); return next; });
        this.reload();
      },
      error: (err) => {
        this.uploading.update((s) => { const next = new Set(s); next.delete(f.name); return next; });
        this.failures.update((x) => [...x, { name: f.name, message: err?.error?.message ?? 'Upload failed.' }]);
      },
    });
  }

  open(img: ProductImage) { this.lightboxImage.set(img); }

  setPrimary(img: ProductImage) {
    if (!this.productId || !this.canEdit) return;
    this.svc.setPrimary(this.productId, img.id).subscribe(() => this.reload());
  }

  remove(img: ProductImage) {
    if (!this.productId || !this.canEdit) return;
    if (img.isPrimary && this.images().length > 1) {
      const ok = confirm('This is the primary image. Another image will be promoted automatically. Continue?');
      if (!ok) return;
    } else if (!confirm('Delete this image?')) {
      return;
    }
    this.svc.delete(this.productId, img.id).subscribe(() => this.reload());
  }

  onImgError(ev: Event) {
    const el = ev.target as HTMLImageElement;
    el.src = this.svc.placeholder();
  }
}
