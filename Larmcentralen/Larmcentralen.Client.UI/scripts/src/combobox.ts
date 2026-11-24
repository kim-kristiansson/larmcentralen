// Keeps the keys a combobox uses (arrows, Enter, Esc) from scrolling the page or submitting the form.
// Blazor still gets the keydown event and moves the selection.
const keys = new Set(["ArrowDown", "ArrowUp", "Enter", "Escape"]);

export function attach(input: HTMLElement): { dispose: () => void } {
  const handler = (e: KeyboardEvent): void => {
    if (keys.has(e.key)) e.preventDefault();
  };
  input.addEventListener("keydown", handler);
  return { dispose: () => input.removeEventListener("keydown", handler) };
}

export function scrollIntoView(id: string): void {
  document.getElementById(id)?.scrollIntoView({ block: "nearest" });
}
