import { themeAlpine } from 'ag-grid-community';

export function createAgGridTheme(isDarkMode: string) {
  return themeAlpine.withParams({
    accentColor: 'var(--mat-table-row-item-label-text-color)',
    backgroundColor: 'var(--accent-50)',
    borderColor: 'var(--accent-300)',
    browserColorScheme: isDarkMode === 'dark' ? 'dark' : 'light',
    wrapperBorder: false,
    chromeBackgroundColor: {
      ref: 'foregroundColor',
      mix: 0.07,
      onto: 'backgroundColor'
    },
    fontFamily: {
      googleFont: 'Public Sans, sans-serif'
    },
    headerFontFamily: {
      googleFont: 'Public Sans, sans-serif'
    },
    foregroundColor: 'var(--mat-table-row-item-label-text-color)',
    rowHoverColor: 'transparent',
    iconButtonHoverColor: 'var(--mat-table-row-item-label-text-color)',
    headerBackgroundColor: 'var(--accent-100)',
    menuBackgroundColor: 'var(--accent-50)',
    headerRowBorder: true,
    rowBorder: true,
    headerFontSize: 12,
    headerTextColor: 'var(--mat-table-row-item-label-text-color)',
    spacing: '8.3px'
  });
}
