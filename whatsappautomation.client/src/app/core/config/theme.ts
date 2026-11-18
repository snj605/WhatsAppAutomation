import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';

/**
 * The approved look (design canvas): deep teal only for main actions, light grey page, white cards with a 1px border.
 * Same colours as the app's theme.
 */
export const AppPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#f0fdf9',
      100: '#ccfbef',
      200: '#99f6df',
      300: '#5eead0',
      400: '#2dd4b6',
      500: '#14b89c',
      600: '#0d9480',
      700: '#0f766e',
      800: '#115e59',
      900: '#134e4a',
      950: '#042f2e',
    },
    colorScheme: {
      light: {
        primary: { color: '#0f766e', contrastColor: '#ffffff', hoverColor: '#115e59', activeColor: '#134e4a' },
        surface: {
          0: '#ffffff',
          50: '#f7f8fa',
          100: '#f2f4f7',
          200: '#eaecf0',
          300: '#d0d5dd',
          400: '#98a2b3',
          500: '#667085',
          600: '#475467',
          700: '#344054',
          800: '#1d2939',
          900: '#101828',
          950: '#0c111d',
        },
      },
      dark: {
        primary: { color: '#2dd4b6', contrastColor: '#042f2e', hoverColor: '#5eead0', activeColor: '#99f6df' },
      },
    },
  },
});

/** Light, dark or the device's choice, remembered in this browser. */
export type ThemeMode = 'light' | 'dark' | 'system';

const KEY = 'theme-mode';

export function savedThemeMode(): ThemeMode {
  try {
    const value = localStorage.getItem(KEY);
    return value === 'light' || value === 'dark' ? value : 'system';
  } catch {
    return 'system';
  }
}

/** Applies the mode to the page (the "app-dark" class switches PrimeNG and the app's own colours). */
export function applyThemeMode(mode: ThemeMode): void {
  try {
    localStorage.setItem(KEY, mode);
  } catch {
    // Storage may be blocked; the choice then lasts until the page is closed.
  }

  const dark = mode === 'dark' || (mode === 'system' && window.matchMedia?.('(prefers-color-scheme: dark)').matches);
  document.documentElement.classList.toggle('app-dark', !!dark);
}
