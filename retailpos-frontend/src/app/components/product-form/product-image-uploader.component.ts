import { CommonModule } from '@angular/common';
import {
  Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal,
} from '@angular/core';
import { ProductImage, PRODUCT_IMAGE_RULES } from '../../models/product-image.model';
import { ProductImageService } from '../../services/product-image.service';
import { ImageLightboxComponent } from './image-lightbox.component';

interface UploadFailure { name: string; message: string; }

@Component({
  selector: 'app-product-image-uploader',
  standalone: true,
  imports: [CommonModule, ImageLightboxComponent],
  template: `
    <div class="space-y-3">
      <div *ngIf="!productId" class="text-sm text-amber-700 bg-amber-50 border border-amber-200 rounded p-3">
        Save the product first to enable image uploads.
      </div>

      <ng-container *ngIf="productId">
        <label
          class="flex flex-col items-center justify-center w-full h-32 border-2 border-dashed
                 border-slate-300 rounded-lg cursor-pointer hover:bg-slate-50">
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

        <div *ngIf="failures().length" class="space-y-1">
          <div *ngFor="let f of failures()" class="text-xs text-red-700 bg-red-50 border border-red-200 rounded p-2">
            <strong>{{ f.name }}:</strong> {{ f.message }}
          </div>
        </div>

        <div *ngIf="uploading().size" class="text-xs text-slate-600">
          Uploading {{ uploading().size }} file(s)…
        </div>

        <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3" *ngIf="images().length">
          <div *ngFor="let img of images()"
               class="relative border rounded overflow-hidden bg-white group">
            <img [src]="svc.resolveUrl(img.thumbUrl)" loading="lazy" decoding="async"
                 (error)="onImgError($event)"
                 class="w-full h-32 object-cover bg-slate-100" />

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
        </div>
      </ng-container>

      <app-image-lightbox *ngIf="lightboxImage()"
        [src]="svc.resolveUrl(lightboxImage()!.mediumUrl)"
        [originalUrl]="svc.resolveUrl(lightboxImage()!.originalUrl)"
        [width]="lightboxImage()!.width"
        [height]="lightboxImage()!.height"
        (closed)="lightboxImage.set(null)"></app-image-lightbox>
    </div>
  `,
})
export class ProductImageUploaderComponent implements OnChanges {
  svc = inject(ProductImageService);

  @Input() productId: number | null = null;
  @Output() primaryChanged = new EventEmitter<ProductImage | null>();

  rules = PRODUCT_IMAGE_RULES;
  images = signal<ProductImage[]>([]);
  uploading = signal<Set<string>>(new Set());
  failures = signal<UploadFailure[]>([]);
  lightboxImage = signal<ProductImage | null>(null);

  ngOnChanges(c: SimpleChanges): void {
    if (c['productId'] && this.productId) this.reload();
  }

  reload() {
    if (!this.productId) return;
    this.svc.list(this.productId).subscribe((imgs) => {
      this.images.set(imgs);
      this.primaryChanged.emit(imgs.find((i) => i.isPrimary) ?? null);
    });
  }

  async onFiles(files: FileList | null) {
    if (!files || !this.productId) return;
    this.failures.set([]);
    for (let i = 0; i < files.length; i++) {
      const f = files.item(i);
      if (!f) continue;
      const err = await this.preValidate(f);
      if (err) {
        this.failures.update((x) => [...x, { name: f.name, message: err }]);
        continue;
      }
      this.uploadOne(f);
    }
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
