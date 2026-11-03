/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/**/*.{html,ts}",
  ],
  // Shared-UI classes (src/styles.css, @layer components) are only referenced from templates that adopt them.
  safelist: [
    { pattern: /^ui-(control|check|form-stack)/ },
  ],
  theme: {
    extend: {
      colors: {
        // Semantic design tokens (see src/styles.css). Use these instead of raw hex or gray-*/slate-* in new UI.
        rp: {
          primary: 'var(--rp-color-primary)',
          action: 'var(--rp-color-action)',
          'action-hover': 'var(--rp-color-action-hover)',
          'on-action': 'var(--rp-color-on-action)',
          link: 'var(--rp-color-text-link)',
          page: 'var(--rp-color-surface-page)',
          card: 'var(--rp-color-surface-card)',
          muted: 'var(--rp-color-surface-muted)',
          hover: 'var(--rp-color-surface-hover)',
          border: 'var(--rp-color-border)',
          'border-control': 'var(--rp-color-border-control)',
          ink: 'var(--rp-color-text-primary)',
          'ink-secondary': 'var(--rp-color-text-secondary)',
          'success-bg': 'var(--rp-color-status-success-bg)',
          'success-fg': 'var(--rp-color-status-success-fg)',
          'warning-bg': 'var(--rp-color-status-warning-bg)',
          'warning-fg': 'var(--rp-color-status-warning-fg)',
          'danger-bg': 'var(--rp-color-status-danger-bg)',
          'danger-fg': 'var(--rp-color-status-danger-fg)',
          'info-bg': 'var(--rp-color-status-info-bg)',
          'info-fg': 'var(--rp-color-status-info-fg)',
        },
        primary: {
          50: '#fafafa',
          100: '#f4f4f5',
          200: '#e4e4e7',
          300: '#d4d4d8',
          400: '#a1a1aa',
          500: '#71717a',
          600: '#52525b',
          700: '#3f3f46',
          800: '#27272a',
          900: '#18181b',
          950: '#09090b',
        },
      },
      boxShadow: {
        'soft': '0 1px 3px 0 rgba(0, 0, 0, 0.1), 0 1px 2px -1px rgba(0, 0, 0, 0.1)',
        'elevated': '0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -4px rgba(0, 0, 0, 0.1)',
        'rp-popover': 'var(--rp-shadow-popover)',
      },
      borderRadius: {
        'rp-control': 'var(--rp-radius-control)',
        'rp-card': 'var(--rp-radius-card)',
      },
      fontFamily: {
        rp: 'var(--rp-font-family-base)',
      },
      animation: {
        'fade-in': 'fadeIn 0.2s ease-in-out',
        'slide-up': 'slideUp 0.2s ease-out',
        'slide-down': 'slideDown 0.2s ease-out',
      },
      keyframes: {
        fadeIn: {
          '0%': { opacity: '0' },
          '100%': { opacity: '1' },
        },
        slideUp: {
          '0%': { transform: 'translateY(10px)', opacity: '0' },
          '100%': { transform: 'translateY(0)', opacity: '1' },
        },
        slideDown: {
          '0%': { transform: 'translateY(-10px)', opacity: '0' },
          '100%': { transform: 'translateY(0)', opacity: '1' },
        },
      },
    },
  },
  plugins: [],
}
