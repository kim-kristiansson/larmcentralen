// Applies the chosen theme to <html>. "auto" removes the override so the OS decides
// (color-scheme: light dark in app.css). The choice is mirrored to localStorage so the
// host page can apply it before Blazor starts (see theme-init.ts).
import { themeKey, type ThemeMode } from "./theme-key";

export function apply(mode: string): void {
  const root = document.documentElement;
  let stored: ThemeMode;
  if (mode === "light" || mode === "dark") {
    root.dataset.theme = mode;
    stored = mode;
  } else {
    delete root.dataset.theme;
    stored = "auto";
  }
  try {
    localStorage.setItem(themeKey, stored);
  } catch {
    // Storage can be unavailable; the theme still applies for this session.
  }
}
