import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ProductImage } from '../../models/product-image.model';
import { ProductImageService } from '../../services/product-image.service';
import { ProductImageUploaderComponent } from './product-image-uploader.component';

const image = (id: number, over: Partial<ProductImage> = {}): ProductImage =>
  ({ id, productId: 5, thumbUrl: `t${id}.jpg`, mediumUrl: `m${id}.jpg`, originalUrl: `o${id}.jpg`, isPrimary: false, width: 900, height: 900, ...over }) as ProductImage;

const fileList = (files: File[]) => ({ length: files.length, item: (i: number) => files[i] }) as unknown as FileList;

describe('ProductImageUploaderComponent', () => {
  let fixture: ComponentFixture<ProductImageUploaderComponent>;
  let cmp: ProductImageUploaderComponent;
  let svc: {
    list: ReturnType<typeof vi.fn>; upload: ReturnType<typeof vi.fn>; delete: ReturnType<typeof vi.fn>; setPrimary: ReturnType<typeof vi.fn>;
    resolveUrl: (p: string | null | undefined) => string; placeholder: () => string;
  };
  let dims = { w: 1000, h: 1000 };
  let listed: ProductImage[];

  const root = () => fixture.nativeElement as HTMLElement;
  const q = <E extends HTMLElement = HTMLElement>(selector: string) => root().querySelector(selector) as E;
  const qa = <E extends HTMLElement = HTMLElement>(selector: string) => Array.from(root().querySelectorAll<E>(selector));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const png = (name = 'a.png', size = 10) => new File([new Uint8Array(size)], name, { type: 'image/png' });

  async function setup(productId: number | null = null) {
    listed = [image(1, { isPrimary: true }), image(2)];
    svc = {
      list: vi.fn(() => of(listed)),
      upload: vi.fn(() => of({})),
      delete: vi.fn(() => of(undefined)),
      setPrimary: vi.fn(() => of({ primaryImageId: 2 })),
      resolveUrl: (p: string | null | undefined) => `/cdn/${p ?? ''}`,
      placeholder: () => '/placeholder.png'
    };
    TestBed.configureTestingModule({ imports: [ProductImageUploaderComponent], providers: [{ provide: ProductImageService, useValue: svc }] });
    fixture = TestBed.createComponent(ProductImageUploaderComponent);
    cmp = fixture.componentInstance;
    fixture.componentRef.setInput('productId', productId);
    await settle();
  }

  beforeEach(() => {
    dims = { w: 1000, h: 1000 };
    URL.createObjectURL = vi.fn(() => 'blob:preview');
    URL.revokeObjectURL = vi.fn();
    vi.stubGlobal(
      'Image',
      class {
        onload: (() => void) | null = null;
        onerror: (() => void) | null = null;
        get naturalWidth() { return dims.w; }
        get naturalHeight() { return dims.h; }
        set src(_: string) { queueMicrotask(() => this.onload?.()); }
      }
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  describe('drop zone', () => {
    beforeEach(() => setup());

    it('is a keyboard-reachable control: the file input is named and not display:none', () => {
      const input = q<HTMLInputElement>('input[type="file"]');
      expect(input.getAttribute('aria-label')).toBe('Upload product images');
      expect(input.classList).toContain('sr-only');
      expect(input.classList).not.toContain('hidden');
      expect(input.multiple).toBe(true);
      expect(input.accept).toBe('image/jpeg,image/jpg,image/png,image/webp');
      expect(q('label.pu-drop').textContent).toContain('Click to upload');
      expect(q('label.pu-drop').textContent).toContain('up to 5 MB');
      expect(q('label.pu-drop').textContent).toContain('Large images are optimized automatically');
      expect(q('label.pu-drop').textContent).not.toContain('800');
    });
  });

  describe('create mode (queued previews)', () => {
    beforeEach(() => setup(null));

    it('queues a valid image with a preview, a Queued flag and a named remove button', async () => {
      await cmp.onFiles(fileList([png('front.png')]));
      await settle();
      expect(cmp.getPendingFiles().map(f => f.name)).toEqual(['front.png']);
      const tile = q('.pu-tile--pending');
      expect(tile.querySelector('img')!.getAttribute('src')).toBe('blob:preview');
      expect(tile.querySelector('img')!.getAttribute('alt')).toBe('Queued image front.png');
      expect(tile.querySelector('.pu-flag')!.textContent).toBe('Queued');
      expect(tile.querySelector('button')!.getAttribute('aria-label')).toBe('Remove queued image front.png');
      expect(q('app-ui-alert[data-variant="warning"]').textContent).toContain('1 image(s) queued — will be uploaded when you save the product.');
      expect(svc.upload).not.toHaveBeenCalled();
    });

    it('removes a queued image and revokes its preview', async () => {
      await cmp.onFiles(fileList([png('a.png'), png('b.png')]));
      await settle();
      q<HTMLButtonElement>('button[aria-label="Remove queued image a.png"]').click();
      await settle();
      expect(cmp.getPendingFiles().map(f => f.name)).toEqual(['b.png']);
      expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:preview');
    });

    it('clears the queue', async () => {
      await cmp.onFiles(fileList([png('a.png')]));
      cmp.clearPendingFiles();
      await settle();
      expect(cmp.getPendingFiles()).toEqual([]);
      expect(q('.pu-grid')).toBeNull();
    });

    it('reads files chosen through the input', async () => {
      const input = q<HTMLInputElement>('input[type="file"]');
      Object.defineProperty(input, 'files', { value: fileList([png('picked.png')]) });
      input.dispatchEvent(new Event('change'));
      await vi.waitFor(() => expect(cmp.getPendingFiles().length).toBe(1));
    });

    const dangerAlerts = () => qa('app-ui-alert[data-variant="danger"]').map(a => a.textContent!.replace(/\s+/g, ' ').trim());

    it.each([
      [1086, 1448],
      [800, 1200],
      [4000, 3000],
      [200, 200],
      [8000, 6000],
    ])('accepts a %i×%i image without asking the user to resize it', async (w, h) => {
      dims = { w, h };
      await cmp.onFiles(fileList([png('photo.png')]));
      await settle();
      expect(cmp.getPendingFiles().map(f => f.name)).toEqual(['photo.png']);
      expect(dangerAlerts()).toEqual([]);
    });

    it('rejects an image below the minimum dimension with the same rule as the server', async () => {
      dims = { w: 150, h: 900 };
      await cmp.onFiles(fileList([png('small.png')]));
      await settle();
      expect(cmp.getPendingFiles()).toEqual([]);
      expect(dangerAlerts()).toEqual(['small.png: Image is too small: each side must be at least 200 px. Got 150×900.']);
    });

    it('rejects an image above the pixel limit with the same rule as the server', async () => {
      dims = { w: 10000, h: 6000 };
      await cmp.onFiles(fileList([png('giant.png')]));
      await settle();
      expect(cmp.getPendingFiles()).toEqual([]);
      expect(dangerAlerts()).toEqual(['giant.png: Image is too large to process: the limit is 50 megapixels. Got 10000×6000.']);
    });

    it('explains an oversize file and a file the browser cannot decode', async () => {
      await cmp.onFiles(fileList([png('huge.png', 5 * 1024 * 1024 + 1)]));
      await settle();
      expect(dangerAlerts()).toEqual(['huge.png: File too large (max 5 MB).']);

      vi.stubGlobal(
        'Image',
        class {
          onerror: (() => void) | null = null;
          set src(_: string) { queueMicrotask(() => this.onerror?.()); }
        }
      );
      await cmp.onFiles(fileList([png('broken.png')]));
      await settle();
      expect(cmp.getPendingFiles()).toEqual([]);
      expect(dangerAlerts()).toEqual(['broken.png: The file is not a valid JPG, PNG, or WEBP image, or it is corrupted.']);
    });


    it('reports each rejection when several are chosen at once', async () => {
      const gif = new File(['x'], 'anim.gif', { type: 'image/gif' });
      await cmp.onFiles(fileList([gif, png('ok.png')]));
      await settle();
      expect(cmp.getPendingFiles().map(f => f.name)).toEqual(['ok.png']);
      expect(q('app-ui-alert[data-variant="danger"]').textContent).toContain('anim.gif: Only JPG, PNG, or WEBP allowed.');
    });
  });

  describe('without products.edit (view-only media)', () => {
    async function setupViewOnly(productId: number | null) {
      await setup(productId);
      fixture.componentRef.setInput('canEdit', false);
      await settle();
    }

    it('hides the drop zone and explains that image changes need the Edit Products permission', async () => {
      await setupViewOnly(null);
      expect(q('.pu-drop')).toBeNull();
      expect(q('input[type="file"]')).toBeNull();
      expect(root().textContent).toContain('Adding, replacing or removing product images requires the Edit Products permission.');
    });

    it('keeps images viewable but offers no set-primary or delete actions', async () => {
      await setupViewOnly(5);
      expect(qa('.pu-tile')).toHaveLength(2);
      expect(qa('.pu-tile button').map(b => b.getAttribute('aria-label'))).toEqual(['View image', 'View image']);
    });

    it('ignores upload, set-primary and delete attempts so no request can fail with 403', async () => {
      await setupViewOnly(5);
      const files = { length: 1, item: () => png() } as unknown as FileList;
      await cmp.onFiles(files);
      cmp.setPrimary(listed[1]);
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      cmp.remove(listed[1]);

      expect(svc.upload).not.toHaveBeenCalled();
      expect(svc.setPrimary).not.toHaveBeenCalled();
      expect(svc.delete).not.toHaveBeenCalled();
      expect(cmp.pendingFiles()).toEqual([]);
    });
  });

  describe('edit mode (uploaded images)', () => {
    beforeEach(() => setup(5));

    it('lists the product images with the primary marked', () => {
      expect(svc.list).toHaveBeenCalledWith(5);
      const tiles = qa('.pu-tile');
      expect(tiles).toHaveLength(2);
      expect(tiles[0].querySelector('img')!.getAttribute('src')).toBe('/cdn/t1.jpg');
      expect(tiles[0].querySelector('.pu-flag')!.textContent).toBe('Primary');
      expect(tiles[1].querySelector('.pu-flag')).toBeNull();
    });

    it('gives every image action an accessible name and keeps them reachable by keyboard', () => {
      const names = qa('.pu-tile:nth-child(2) button').map(b => b.getAttribute('aria-label'));
      expect(names).toEqual(['View image', 'Set as primary image', 'Delete image']);
      expect(qa('.pu-tile:first-child button').map(b => b.getAttribute('aria-label'))).toEqual(['View image', 'Delete image']);
      for (const b of qa<HTMLButtonElement>('.pu-tools button')) {
        expect(b.type).toBe('button');
        expect(b.tabIndex).toBe(0);
      }
    });

    it('sets a new primary image and reloads', async () => {
      q<HTMLButtonElement>('.pu-tile:nth-child(2) button[aria-label="Set as primary image"]').click();
      expect(svc.setPrimary).toHaveBeenCalledWith(5, 2);
      expect(svc.list).toHaveBeenCalledTimes(2);
    });

    it('opens the lightbox for a preview', async () => {
      q<HTMLButtonElement>('.pu-tile:first-child button[aria-label="View image"]').click();
      await settle();
      expect(q('app-image-lightbox')).not.toBeNull();
    });

    it('confirms before deleting and only deletes when accepted', async () => {
      const confirm = vi.spyOn(window, 'confirm').mockReturnValueOnce(false).mockReturnValueOnce(true);
      q<HTMLButtonElement>('.pu-tile:nth-child(2) button[aria-label="Delete image"]').click();
      expect(confirm).toHaveBeenLastCalledWith('Delete this image?');
      expect(svc.delete).not.toHaveBeenCalled();
      q<HTMLButtonElement>('.pu-tile:nth-child(2) button[aria-label="Delete image"]').click();
      expect(svc.delete).toHaveBeenCalledWith(5, 2);
    });

    it('warns before deleting the primary image when others exist', () => {
      const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
      q<HTMLButtonElement>('.pu-tile:first-child button[aria-label="Delete image"]').click();
      expect(confirm).toHaveBeenCalledWith('This is the primary image. Another image will be promoted automatically. Continue?');
      expect(svc.delete).not.toHaveBeenCalled();
    });

    it('uploads valid files immediately and reloads the list', async () => {
      await cmp.onFiles(fileList([png('new.png')]));
      expect(svc.upload).toHaveBeenCalledWith(5, expect.objectContaining({ name: 'new.png' }));
      expect(svc.list).toHaveBeenCalledTimes(2);
      expect(cmp.getPendingFiles()).toEqual([]);
    });

    it('shows an upload failure with the server message and a progress status while uploading', async () => {
      svc.upload.mockReturnValueOnce(throwError(() => ({ error: { message: 'Duplicate image' } })));
      await cmp.onFiles(fileList([png('dup.png')]));
      await settle();
      expect(q('app-ui-alert[data-variant="danger"]').textContent).toContain('dup.png: Duplicate image');

      cmp.uploading.set(new Set(['x.png', 'y.png']));
      await settle();
      expect(q('.pu-progress').getAttribute('role')).toBe('status');
      expect(q('.pu-progress').textContent).toContain('Uploading 2 file(s)');
    });

    it('does not show the queue notice in edit mode', async () => {
      await settle();
      expect(q('app-ui-alert[data-variant="warning"]')).toBeNull();
    });
  });
});
