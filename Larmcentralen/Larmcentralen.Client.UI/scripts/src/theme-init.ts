// Runs before the first paint so a forced light or dark theme does not flash.
// Built as a classic script (iife) into Larmcentralen.App/wwwroot/theme-init.js.
import { themeKey } from "./theme-key";

try {
  const mode = localStorage.getItem(themeKey);
  if (mode === "light" || mode === "dark") document.documentElement.dataset.theme = mode;
} catch {
  // Storage unavailable: follow the OS.
}
