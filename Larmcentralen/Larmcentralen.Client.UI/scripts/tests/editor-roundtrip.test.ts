// Round-trip tests for the editor as the app builds it (editor-setup.ts): loading Markdown and saving it again must
// give the same text, and text in another style must settle after one save. Runs in jsdom, no browser needed.
import { test, before } from "node:test";
import assert from "node:assert/strict";
import { JSDOM } from "jsdom";

type Setup = typeof import("../src/editor-setup");
let setup: Setup;

before(async () => {
  const { window } = new JSDOM("<!doctype html><html><body></body></html>", { pretendToBeVisual: true });
  // Make the jsdom window the global scope, as in a browser: every window property node doesn't have itself
  // (document, HTMLElement, dispatchEvent, …), plus the ones node has its own version of.
  const own = new Set(["window", "document", "navigator", "getComputedStyle", "requestAnimationFrame", "cancelAnimationFrame", "Event", "CustomEvent", "EventTarget"]);
  const win = window as unknown as Record<string, unknown>;
  for (const name of Object.getOwnPropertyNames(window)) {
    if (name in globalThis && !own.has(name)) continue;
    let value: unknown;
    try {
      value = name === "window" ? window : win[name];
    } catch {
      continue; // e.g. localStorage on an opaque origin
    }
    Object.defineProperty(globalThis, name, {
      value: typeof value === "function" && !/^[A-Z]/.test(name) ? (value as (...a: unknown[]) => unknown).bind(window) : value,
      configurable: true,
      writable: true,
    });
  }
  for (const name of ["addEventListener", "removeEventListener", "dispatchEvent"]) {
    Object.defineProperty(globalThis, name, { value: (win[name] as (...a: unknown[]) => unknown).bind(window), configurable: true, writable: true });
  }
  // Loaded after the DOM globals exist: ProseMirror reads them when it loads.
  setup = await import("../src/editor-setup");
});

async function save(markdown: string): Promise<string> {
  const root = document.createElement("div");
  document.body.appendChild(root);
  const editor = await setup.createEditor(root, markdown);
  const out = setup.markdownOf(editor);
  await editor.destroy();
  root.remove();
  return out;
}

/** Text already in the editor's style: saving it changes nothing. */
const canonical: Record<string, string> = {
  "headings and paragraphs": "## Felsökning\n\nKontrollera först att ugnen är i manuellt läge.\n\n### Om felet kommer tillbaka\n\nRing underhåll.",
  "bullet list": "- Handskar\n- Skyddsglasögon\n- Hörselskydd",
  "numbered list": "1. Stäng av ugnen\n2. Vänta 10 min\n3. Öppna luckan",
  "numbered list from 3": "3. Tre\n4. Fyra",
  "nested list": "1. Öppna skåpet\n   - Kontrollera säkringen\n   - Mät spänningen\n2. Stäng skåpet",
  "plain quote (neutral)": "> Bryt strömmen innan skåpet öppnas!",
  "Fara box": "> [!CAUTION]\n> Het yta, risk för brännskada. Bär handskar.",
  "Varning box": "> [!WARNING]\n> Bryt strömmen innan skåpet öppnas!",
  "Info box": "> [!NOTE]\n> Ugnen behöver 10 minuter för att svalna.",
  "Info box with a list": "> [!NOTE]\n> Kontrollera:\n>\n> - Säkringen\n> - Givaren",
  "Fara, Varning and Info after each other": "> [!CAUTION]\n> Gas!\n\n> [!WARNING]\n> Het yta!\n\n> [!NOTE]\n> Bär handskar.",
  "bold, italic, strike": "**Fet** och *kursiv* och ~~struken~~.",
  "inline code": "Sätt `P-204` till 0.",
  "code block": "```\nRESET 1\nSTART\n```",
  "code block with language": "```plc\nRESET 1\n```",
  "rule": "Före\n\n---\n\nEfter",
  "link": "Se [manualen](https://example.com/manual.pdf).",
  "relative link": "Se [larmet](/larm/12).",
  "image and video": "![Skåp](bilder/skap.png)\n\n![Byte av givare](film/givare.mp4)",
  "task list": "- [x] Klart\n- [ ] Kvar",
  "hard break": "Rad ett\\\nRad två",
  "table with alignment": "| Givare | Värde | Enhet |\n| :- | :-: | -: |\n| TE-101 | 4–20 | mA |\n| TE-102 | 0–10 | V |",
  "escaped characters": "\\*inte kursiv\\*\n\n2024\\. var ett år.",
  "swedish text": "Ånga, älv, ö: 850 °C, 50 % öppen.",
};

for (const [name, markdown] of Object.entries(canonical)) {
  test(`unchanged after save: ${name}`, async () => {
    assert.equal(await save(markdown), markdown);
  });
}

/** Other ways of writing the same thing: one save gives the editor's style, and a second save changes nothing. */
const normalised: [string, string, string][] = [
  ["* bullets", "* Handskar\n* Skyddsglasögon", "- Handskar\n- Skyddsglasögon"],
  ["_ emphasis and __ bold", "_kursiv_ och __fet__", "*kursiv* och **fet**"],
  ["*** rule", "Före\n\n***\n\nEfter", "Före\n\n---\n\nEfter"],
  ["padded table", "| A   | B   |\n| :-- | --: |\n| 1   | 2   |", "| A | B |\n| :- | -: |\n| 1 | 2 |"],
  ["two-space hard break", "Rad ett  \nRad två", "Rad ett\\\nRad två"],
  ["lower-case caution", "> [!caution]\n> Gas!", "> [!CAUTION]\n> Gas!"],
  ["IMPORTANT alert", "> [!IMPORTANT]\n> Läs först.", "> [!NOTE]\n> Läs först."],
  ["TIP alert in lower case", "> [!tip]\n> Bär handskar.", "> [!NOTE]\n> Bär handskar."],
  ["image title that repeats its text", "![Film](film/givare.mp4 \"Film\")", "![Film](film/givare.mp4)"],
];

for (const [name, input, expected] of normalised) {
  test(`settles after one save: ${name}`, async () => {
    const once = await save(input);
    assert.equal(once, expected);
    assert.equal(await save(once), once);
  });
}

test("javascript: and data: links and images are removed on save, link text kept", async () => {
  const out = await save("Se [klicka](javascript:alert(1)) och ![bild](data:image/png;base64,AAAA) och [ok](https://example.com).");
  assert.doesNotMatch(out, /javascript:|data:/i);
  assert.match(out, /klicka/);
  assert.match(out, /\[ok\]\(https:\/\/example\.com\)/);
});

test("an .mp4 image is shown as a video in the editor", async () => {
  const root = document.createElement("div");
  document.body.appendChild(root);
  const editor = await setup.createEditor(root, "![Byte av givare](film/givare.mp4)\n\n![Skåp](bilder/skap.png)");
  const video = root.querySelector("video");
  assert.ok(video, "a video element");
  assert.equal(video!.getAttribute("src"), "film/givare.mp4");
  assert.equal(root.querySelectorAll("img[src]").length, 1, "the .png stays an image");
  await editor.destroy();
  root.remove();
});

test("Fara, Varning and Info boxes are blockquotes with their kind in the editor, and no [!...] text", async () => {
  const root = document.createElement("div");
  document.body.appendChild(root);
  const editor = await setup.createEditor(root, "> [!CAUTION]\n> Gas!\n\n> [!WARNING]\n> Het yta!\n\n> [!NOTE]\n> Bär handskar.\n\n> Citat");
  const quotes = Array.from(root.querySelectorAll("blockquote")).map((q) => q.getAttribute("data-alert") ?? "plain");
  assert.deepEqual(quotes, ["caution", "warning", "note", "plain"]);
  assert.doesNotMatch(root.textContent ?? "", /\[!/);
  await editor.destroy();
  root.remove();
});

const razorPath = new URL("../../Components/MarkdownEditor.razor", import.meta.url);

/** The editor's markup as the Razor component renders it (Razor attributes and comments removed). */
async function editorMarkup(): Promise<string> {
  const { readFileSync } = await import("node:fs");
  const razor = readFileSync(razorPath, "utf8");
  const start = razor.indexOf('<div class="md-editor"');
  const end = razor.indexOf("@if (_loadError");
  return razor.slice(start, end).replace(/@ref="[^"]*"/g, "").replace(/@\*[\s\S]*?\*@/g, "");
}

test("one Ruta button holds Fara, Varning, Info and Ingen ruta, in that order, in Swedish", async () => {
  const host = document.createElement("div");
  host.innerHTML = await editorMarkup();
  const button = host.querySelector('[data-cmd="box"]');
  assert.equal(button?.getAttribute("title"), "Ruta");
  assert.equal(button?.getAttribute("aria-haspopup"), "menu");
  const items = Array.from(host.querySelectorAll(".md-box-item")).map((i) => `${i.getAttribute("data-box")}:${i.querySelector("span")?.textContent}`);
  assert.deepEqual(items, ["caution:Fara", "warning:Varning", "note:Info", "none:Ingen ruta"]);
  assert.equal(host.querySelectorAll('[data-cmd="caution"], [data-cmd="warning"], [data-cmd="note"]').length, 0, "no separate box buttons any more");
});

test("the Ruta menu applies, switches and removes a box, with the mouse and the keyboard", async () => {
  Object.defineProperty(globalThis, "ResizeObserver", {
    value: class { observe() {} unobserve() {} disconnect() {} },
    configurable: true,
    writable: true,
  });
  const interop = await import("../src/milkdown-interop");
  const host = document.createElement("div");
  host.innerHTML = await editorMarkup();
  document.body.appendChild(host);
  const root = host.querySelector<HTMLElement>(".md-editor")!;
  const handle = await interop.create(root, "Het yta.", "", "Åtgärd");
  const button = root.querySelector<HTMLButtonElement>('[data-cmd="box"]')!;
  const menu = root.querySelector<HTMLElement>(".md-box-menu")!;
  const item = (kind: string) => menu.querySelector<HTMLButtonElement>(`[data-box="${kind}"]`)!;
  const key = (target: Element, k: string) => target.dispatchEvent(new KeyboardEvent("keydown", { key: k, bubbles: true, cancelable: true }));

  assert.equal(menu.hidden, true);
  assert.equal(item("none").disabled, true, "Ingen ruta is off outside a box");
  button.click();
  assert.equal(menu.hidden, false, "the button opens the menu");
  item("caution").click();
  assert.equal(menu.hidden, true, "choosing closes the menu");
  assert.equal(interop.getMarkdown(handle), "> [!CAUTION]\n> Het yta.");
  assert.equal(button.dataset["kind"], "caution");
  assert.equal(button.title, "Ruta: Fara");
  assert.equal(item("caution").getAttribute("aria-checked"), "true");

  button.click();
  item("note").click();
  assert.equal(interop.getMarkdown(handle), "> [!NOTE]\n> Het yta.", "switches the kind");
  button.click();
  item("none").click();
  assert.equal(interop.getMarkdown(handle), "Het yta.", "Ingen ruta removes the box");
  assert.equal(button.title, "Ruta");

  // Keyboard: ArrowDown on the button opens the menu and focuses the first item; arrows move; Escape returns focus.
  button.focus();
  key(button, "ArrowDown");
  assert.equal(menu.hidden, false);
  assert.equal(document.activeElement, item("caution"));
  key(document.activeElement!, "ArrowDown");
  assert.equal(document.activeElement, item("warning"));
  (document.activeElement as HTMLButtonElement).click();
  assert.equal(interop.getMarkdown(handle), "> [!WARNING]\n> Het yta.");
  key(button, "ArrowDown");
  assert.equal(document.activeElement, item("warning"), "the current kind gets focus");
  key(document.activeElement!, "Escape");
  assert.equal(menu.hidden, true);
  assert.equal(document.activeElement, button, "Escape returns focus to the button");

  interop.destroy(handle);
  host.remove();
});