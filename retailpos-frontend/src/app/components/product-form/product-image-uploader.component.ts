import { CommonModule } from '@angular/common';
import {
  Component, EventEmitter, Input, OnChanges, OnDestroy, Output, SimpleChanges, inject, signal,
} from '@angular/core';
import { ProductImage, PRODUCT_IMAGE_RULES } from '../../models/product-image.model';
import { ProductImageService } from '../../services/product-image.service';
import { ImageLightboxComponent } from './image-lightbox.component';

interface UploadFailure { name: string; message: string; }
interface PendingFile { file: File; previewUrl: string; }

@Component({
  selector: 'app-product-image-uploader',
  standalone: true,
  imports: [CommonModule, ImageLightboxComponent],
  template: `
    <div class="space-y-3">

      <!-- Drop zone: always visible (pending queue in create mode) -->
      <label
        class="flex flex-col items-center justify-center w-full h-28 border-2 border-dashed
               border-slate-300 rounded-lg cursor-pointer hover:bg-slate-50 transition-colors">
        <span class="text-sm text-slate-600">
          <strong>Click to upload</strong> or drop images here
        </span>
        <span class="text-xs text-slate-400 mt-1">
          JPG / PNG / WEBP, {{ rules.minDim }}–{{ rules.maxDim }} px, &lt;{{ rules.maxBytes / (1024*1024) }} MB
        </span>
        <input type="file" class="hidden" multiple
               [accept]="rules.allowedMimes.join(',')"
               (change)="onFiles($any($event.target).files)" />
      </label>

      <!-- Validation failures -->
      <div *ngIf="failures().length" class="space-y-1">
        <div *ngFor="let f of failures()" class="text-xs text-red-700 bg-red-50 border border-red-200 rounded p-2">
          <strong>{{ f.name }}:</strong> {{ f.message }}
        </div>
      </div>

      <!-- Upload progress (edit mode) -->
      <div *ngIf="uploading().size" class="text-xs text-slate-500 flex items-center gap-1.5">
        <svg class="animate-spin h-3 w-3 text-slate-500" fill="none" viewBox="0 0 24 24">
          <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
          <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z"></path>
        </svg>
        Uploading {{ uploading().size }} file(s)…
      </div>

      <!-- Pending notice (create mode) -->
      <div *ngIf="!productId && pendingFiles().length"
           class="text-xs text-amber-700 bg-amber-50 border border-amber-200 rounded px-2 py-1.5 flex items-center gap-1.5">
        <span>&#9888;</span>
        {{ pendingFiles().length }} image(s) queued — will be uploaded when you save the product.
      </div>

      <!-- Image grid: uploaded (edit) + pending (create) -->
      <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3"
           *ngIf="images().length || pendingFiles().length">

        <!-- Uploaded images (edit mode) -->
        <div *ngFor="let img of images()"
             class="relative border rounded overflow-hidden bg-white group">
          <img [src]="svc.resolveUrl(img.thumbUrl)" loading="lazy" decoding="async"
               (error)="onImgError($event)"
               class="w-full h-28 object-cover bg-slate-100" />

          <div *ngIf="img.isPrimary"
               class="absolute top-1 left-1 px-1.5 py-0.5 text-[10px] bg-indigo-600 text-white rounded">
            Primary
          </div>

          <div class="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100
                      transition-opacity flex items-center justify-center gap-2">
            <button type="button" title="View"
                    class="p-1 text-white bg-white/20 rounded hover:bg-white/30"
                    (click)="open(img)">
              <span class="text-base">&#128065;</span>
            </button>
            <button type="button" title="Set primary" *ngIf="!img.isPrimary"
                    class="p-1 text-white bg-white/20 rounded hover:bg-white/30"
                    (click)="setPrimary(img)">
              <span class="text-base">&#9733;</span>
            </button>
            <button type="button" title="Delete"
                    class="p-1 text-white bg-red-500/80 rounded hover:bg-red-500"
                    (click)="remove(img)">
              <span class="text-base">&#10005;</span>
            </button>
          </div>
        </div>

        <!-- Pending images (create mode — queued, not yet uploaded) -->
        <div *ngFor="let p of pendingFiles(); let i = index"
             class="relative border border-amber-300 rounded overflow-hidden bg-amber-50 group">
          <img [src]="p.previewUrl"
               class="w-full h-28 object-cover" />

          <div class="absolute top-1 left-1 px-1.5 py-0.5 text-[10px] bg-amber-500 text-white rounded">
            Queued
          </div>

          <div class="absolute inset-0 bg-black/40 opacity-0 group-hover:opacity-100
                      transition-opacity flex items-center justify-center">
            <button type="button" title="Remove"
                    class="p-1 text-white bg-red-500/80 rounded hover:bg-red-500"
                    (click)="removePending(i)">
              <span class="text-base">&#10005;</span>
            </button>
          </div>
        </div>

      </div>
    </div>

    <app-image-lightbox *ngIf="lightboxImage()"
      [src]="svc.resolveUrl(lightboxImage()!.mediumUrl)"
      [originalUrl]="svc.resolveUrl(lightboxImage()!.originalUrl)"
      [width]="lightboxImage()!.width"
      [height]="lightboxImage()!.height"
      (closed)="lightboxImage.set(null)"></app-image-lightbox>
  `,
})
export class ProductImageUploaderComponent implements OnChanges, OnDestroy {
  svc = inject(ProductImageService);

  @Input() productId: number | null = null;
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
    if (!files) return;
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
    if (!dim) return 'Could not read image dimensions.';
    if (dim.w < this.rules.minDim || dim.h < this.rules.minDim
        || dim.w > this.rules.maxDim || dim.h > this.rules.maxDim) {
      return `Dimensions must be ${this.rules.minDim}–${this.rules.maxDim} px on each side. Got ${dim.w}×${dim.h}.`;
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
    if (!this.productId) return;
    this.svc.setPrimary(this.productId, img.id).subscribe(() => this.reload());
  }

  remove(img: ProductImage) {
    if (!this.productId) return;
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
