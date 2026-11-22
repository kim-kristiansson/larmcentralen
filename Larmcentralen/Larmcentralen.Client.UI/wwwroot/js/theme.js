// Applies the chosen theme to <html>. "auto" removes the override so the OS decides
// (color-scheme: light dark in app.css). The choice is mirrored to localStorage so the
// host page can apply it before Blazor starts (see theme-init.js in the app's wwwroot).
const key = "larmcentralen-theme";

export function apply(mode) {
  const root = document.documentElement;
  if (mode === "light" || mode === "dark") {
    root.dataset.theme = mode;
  } else {
    delete root.dataset.theme;
    mode = "auto";
  }
  try {
    localStorage.setItem(key, mode);
  } catch {
    // Storage can be unavailable; the theme still applies for this session.
  }
}
