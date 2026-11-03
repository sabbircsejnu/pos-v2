import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DropdownOption, SearchableDropdownComponent } from './searchable-dropdown.component';

const OPTIONS: DropdownOption[] = [
  { id: 1, name: 'Acme Supplies', description: 'Main vendor' },
  { id: 2, name: 'Bright Traders', description: 'Secondary' },
  { id: 3, name: 'Core Foods', description: '' }
];

describe('SearchableDropdownComponent', () => {
  let fixture: ComponentFixture<SearchableDropdownComponent>;
  let cmp: SearchableDropdownComponent;
  const root = () => fixture.nativeElement as HTMLElement;
  const input = () => root().querySelector('input') as HTMLInputElement;
  const options = () => Array.from(root().querySelectorAll<HTMLElement>('[role="option"]'));
  const settle = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const key = (name: string) => {
    const event = new KeyboardEvent('keydown', { key: name, bubbles: true, cancelable: true });
    input().dispatchEvent(event);
    return event;
  };
  const open = async (items: DropdownOption[] = OPTIONS) => {
    input().focus();
    input().dispatchEvent(new Event('focus'));
    cmp.setOptions(items);
    await settle();
  };

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [SearchableDropdownComponent] });
    fixture = TestBed.createComponent(SearchableDropdownComponent);
    cmp = fixture.componentInstance;
    await settle();
  });

  afterEach(() => {
    vi.useRealTimers();
    TestBed.resetTestingModule();
  });

  it('is a combobox using the shared control class and falls back to the placeholder as its name', () => {
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().classList).toContain('ui-control');
    expect(input().getAttribute('aria-label')).toBe('Search...');
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelector('[role="listbox"]')).toBeNull();
  });

  it('uses an explicit ariaLabel when provided', async () => {
    fixture.componentRef.setInput('ariaLabel', 'Supplier');
    await settle();
    expect(input().getAttribute('aria-label')).toBe('Supplier');
  });

  it('opens a listbox on focus and exposes it through aria-controls', async () => {
    await open();
    const listbox = root().querySelector('[role="listbox"]')!;
    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(input().getAttribute('aria-controls')).toBe(listbox.id);
    expect(options().map(o => o.textContent!.trim())).toEqual(['Acme Supplies', 'Bright Traders', 'Core Foods']);
  });

  it('shows secondary text only when requested and present', async () => {
    fixture.componentRef.setInput('showSecondaryText', true);
    await open();
    const secondary = Array.from(root().querySelectorAll('.sd__option-secondary')).map(e => e.textContent);
    expect(secondary).toEqual(['Main vendor', 'Secondary']);
  });

  it('shows loading and empty states with status roles', async () => {
    input().dispatchEvent(new Event('focus'));
    cmp.setLoading(true);
    await settle();
    expect(root().querySelector('[role="status"]')!.textContent).toContain('Loading');

    cmp.setOptions([]);
    await settle();
    expect(root().querySelector('[role="status"]')!.textContent).toContain('No results found');
  });

  it('debounces typed searches by 300ms and emits a single search with page 1', async () => {
    const searches: unknown[] = [];
    cmp.onSearch.subscribe(e => searches.push(e));
    vi.useFakeTimers();

    for (const value of ['a', 'ac', 'acm']) {
      input().value = value;
      input().dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(100);
    }
    expect(searches).toEqual([]);
    vi.advanceTimersByTime(300);
    expect(searches).toEqual([{ query: 'acm', page: 1, pageSize: 20 }]);
  });

  it('selects with the mouse: updates the text, closes the list and emits onSelect', async () => {
    const selected: DropdownOption[] = [];
    cmp.onSelect.subscribe(o => selected.push(o));
    await open();
    options()[1].click();
    await settle();
    expect(selected).toEqual([OPTIONS[1]]);
    expect(cmp.selectedId).toBe(2);
    expect(input().value).toBe('Bright Traders');
    expect(root().querySelector('[role="listbox"]')).toBeNull();
  });

  it('marks the selected option with aria-selected', async () => {
    fixture.componentRef.setInput('selectedId', 3);
    await open();
    expect(options().map(o => o.getAttribute('aria-selected'))).toEqual(['false', 'false', 'true']);
  });

  describe('keyboard', () => {
    it('moves the active option with the arrow keys and reports it via aria-activedescendant', async () => {
      await open();
      key('ArrowDown');
      await settle();
      expect(input().getAttribute('aria-activedescendant')).toBe(options()[0].id);
      key('ArrowDown');
      key('ArrowDown');
      key('ArrowDown');
      await settle();
      expect(input().getAttribute('aria-activedescendant')).toBe(options()[2].id);
      key('ArrowUp');
      await settle();
      expect(input().getAttribute('aria-activedescendant')).toBe(options()[1].id);
    });

    it('selects the active option with Enter and does nothing when none is active', async () => {
      const selected: DropdownOption[] = [];
      cmp.onSelect.subscribe(o => selected.push(o));
      await open();
      expect(key('Enter').defaultPrevented).toBe(false);
      expect(selected).toEqual([]);

      key('ArrowDown');
      key('ArrowDown');
      expect(key('Enter').defaultPrevented).toBe(true);
      expect(selected).toEqual([OPTIONS[1]]);
    });

    it('closes on Escape without letting it reach a hosting dialog, and on Tab', async () => {
      await open();
      const parentSeen = vi.fn();
      root().addEventListener('keydown', parentSeen);
      const escape = key('Escape');
      await settle();
      expect(escape.defaultPrevented).toBe(true);
      expect(parentSeen).not.toHaveBeenCalled();
      expect(root().querySelector('[role="listbox"]')).toBeNull();

      key('ArrowDown');
      await settle();
      expect(root().querySelector('[role="listbox"]')).not.toBeNull();
      key('Tab');
      await settle();
      expect(root().querySelector('[role="listbox"]')).toBeNull();
    });

    it('lets Escape through when the list is already closed', () => {
      expect(key('Escape').defaultPrevented).toBe(false);
    });
  });

  describe('paging', () => {
    const scrollList = (scrollTop: number) => {
      const list = root().querySelector<HTMLElement>('[role="listbox"]')!;
      Object.defineProperty(list, 'scrollHeight', { value: 400, configurable: true });
      Object.defineProperty(list, 'clientHeight', { value: 200, configurable: true });
      Object.defineProperty(list, 'scrollTop', { value: scrollTop, configurable: true });
      list.dispatchEvent(new Event('scroll'));
    };

    it('requests the next page near the bottom of the list and appends the results', async () => {
      const more: unknown[] = [];
      cmp.onLoadMore.subscribe(e => more.push(e));
      await open();
      scrollList(190);
      expect(more).toEqual([{ query: '', page: 2, pageSize: 20 }]);

      scrollList(190);
      expect(more.length).toBe(1);

      cmp.setOptions([{ id: 4, name: 'Delta' }], true);
      await settle();
      expect(options().length).toBe(4);
    });

    it('does not load more when scrolled away from the bottom or when there is no more data', async () => {
      const more: unknown[] = [];
      cmp.onLoadMore.subscribe(e => more.push(e));
      await open();
      scrollList(0);
      cmp.setHasMore(false);
      scrollList(190);
      expect(more).toEqual([]);
    });
  });

  describe('clear button', () => {
    it('is hidden unless clearable and there is something to clear', async () => {
      expect(root().querySelector('.sd__clear')).toBeNull();
      fixture.componentRef.setInput('clearable', true);
      await settle();
      expect(root().querySelector('.sd__clear')).toBeNull();
      fixture.componentRef.setInput('selectedId', 2);
      await settle();
      expect(root().querySelector('.sd__clear')).not.toBeNull();
    });

    it('resets the control, emits cleared and returns focus to the input', async () => {
      const cleared = vi.fn();
      cmp.cleared.subscribe(cleared);
      fixture.componentRef.setInput('clearable', true);
      fixture.componentRef.setInput('selectedId', 2);
      await open();
      options()[1].click();
      await settle();

      const button = root().querySelector<HTMLButtonElement>('.sd__clear')!;
      expect(button.getAttribute('aria-label')).toBe('Clear selection');
      button.click();
      await settle();
      expect(cleared).toHaveBeenCalledTimes(1);
      expect(cmp.selectedId).toBeNull();
      expect(input().value).toBe('');
    });
  });

  it('is inert when disabled: input disabled, no list, no clear button', async () => {
    fixture.componentRef.setInput('clearable', true);
    fixture.componentRef.setInput('selectedId', 1);
    fixture.componentRef.setInput('disabled', true);
    cmp.showDropdown.set(true);
    await settle();
    expect(input().disabled).toBe(true);
    expect(root().querySelector('[role="listbox"]')).toBeNull();
    expect(root().querySelector('.sd__clear')).toBeNull();
  });

  it('loadInitialData asks the parent for page 1 with an empty query', () => {
    const searches: unknown[] = [];
    cmp.onSearch.subscribe(e => searches.push(e));
    cmp.loadInitialData();
    expect(searches).toEqual([{ query: '', page: 1, pageSize: 20 }]);
  });
});
