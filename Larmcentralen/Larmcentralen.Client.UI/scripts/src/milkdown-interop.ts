// Markdown editing for åtgärder with Milkdown core (ProseMirror underneath). Loaded on demand by
// MarkdownEditor.razor. The toolbar, the link bar and every Swedish text are rendered by the Razor
// component; this module wires them to the editor and keeps their pressed/selected state current.
import { editorViewCtx, editorViewOptionsCtx, type Editor } from "@milkdown/kit/core";
import {
  toggleStrongCommand,
  toggleEmphasisCommand,
  toggleInlineCodeCommand,
  wrapInHeadingCommand,
  turnIntoTextCommand,
  wrapInBulletListCommand,
  wrapInOrderedListCommand,
  liftListItemCommand,
  createCodeBlockCommand,
} from "@milkdown/kit/preset/commonmark";
import { toggleStrikethroughCommand, insertTableCommand, addRowAfterCommand } from "@milkdown/kit/preset/gfm";
import { undoCommand, redoCommand } from "@milkdown/kit/plugin/history";
import { $prose, callCommand } from "@milkdown/kit/utils";
import { Plugin, PluginKey, TextSelection, type EditorState } from "@milkdown/kit/prose/state";
import { Decoration, DecorationSet, type EditorView } from "@milkdown/kit/prose/view";
import { findWrapping, liftTarget } from "@milkdown/kit/prose/transform";
import type { MarkType, Node as ProseNode, ResolvedPos } from "@milkdown/kit/prose/model";
import { readTable, nextCellOrNewRow } from "./table-tools";
import { createTableOverlay } from "./table-overlay";
import { createEditor, markdownOf } from "./editor-setup";
import { isAllowedUrl } from "./url";
import type { AlertKind } from "./alerts";

export { isAllowedUrl } from "./url";

export interface MarkdownEditorHandle {
  editor: Editor;
  dispose: () => void;
}

type Block = "p" | "h2" | "h3";

interface ToolbarState {
  block: Block | "";
  marks: Record<"strong" | "emphasis" | "strike_through" | "inlineCode" | "link", boolean>;
  bullet: boolean;
  ordered: boolean;
  /** The alert kind of the blockquote the cursor is in: "caution", "warning", "note", "plain" (no kind) or "". */
  quote: AlertKind | "plain" | "";
  codeBlock: boolean;
}

export async function create(root: HTMLElement, markdown: string, placeholder: string, label = "Åtgärd"): Promise<MarkdownEditorHandle> {
  const surface = root.querySelector<HTMLElement>(".md-surface");
  const toolbar = root.querySelector<HTMLElement>(".md-toolbar");
  const linkBar = root.querySelector<HTMLElement>(".md-linkbar");
  if (!surface || !toolbar || !linkBar) throw new Error("MarkdownEditor markup is missing");

  let refresh: (view: EditorView) => void = () => {};

  const toolbarPlugin = $prose(
    () =>
      new Plugin({
        key: new PluginKey("larm-toolbar"),
        view: (view) => {
          refresh(view);
          return { update: (v) => refresh(v) };
        },
      }),
  );

  // Shows the placeholder text in the first paragraph while the document is empty.
  const placeholderPlugin = $prose(
    () =>
      new Plugin({
        key: new PluginKey("larm-placeholder"),
        props: {
          decorations: (state) => {
            const first = state.doc.firstChild;
            if (state.doc.childCount !== 1 || !first || first.type.name !== "paragraph" || first.content.size > 0) return null;
            return DecorationSet.create(state.doc, [
              Decoration.node(0, first.nodeSize, { class: "md-placeholder", "data-placeholder": placeholder }),
            ]);
          },
        },
      }),
  );

  const editor = await createEditor(surface, markdown, {
    plugins: [toolbarPlugin, placeholderPlugin],
    config: (ctx) => {
      ctx.update(editorViewOptionsCtx, (prev) => ({
        ...prev,
        attributes: { class: "md-content rich-content", spellcheck: "true", lang: "sv", "aria-multiline": "true", role: "textbox", "aria-label": label },
        // Keep the cursor (and a table's column grips) clear of the sticky toolbar when the editor scrolls to it.
        scrollMargin: { top: toolbar.offsetHeight + 48, bottom: 64, left: 8, right: 8 },
        scrollThreshold: { top: toolbar.offsetHeight + 48, bottom: 64, left: 8, right: 8 },
      }));
    },
  });

  const view = (): EditorView => editor.ctx.get(editorViewCtx);
  const run = (key: Parameters<typeof callCommand>[0], payload?: unknown): void => {
    editor.action(callCommand(key, payload));
  };

  const buttons = Array.from(toolbar.querySelectorAll<HTMLButtonElement>("button[data-cmd]"));
  const blockSelect = toolbar.querySelector<HTMLSelectElement>("select[data-cmd='block']");
  const boxButton = toolbar.querySelector<HTMLButtonElement>("button[data-cmd='box']");
  const boxMenu = toolbar.querySelector<HTMLElement>(".md-box-menu") ?? document.createElement("span");
  const boxItems = Array.from(boxMenu.querySelectorAll<HTMLButtonElement>(".md-box-item"));
  const linkInput = linkBar.querySelector<HTMLInputElement>("input");
  const linkError = linkBar.querySelector<HTMLElement>(".md-link-error");
  let ready = false;
  // Buttons on the table itself (rows, columns, alignment, remove).
  const tables = createTableOverlay(surface, toolbar, () => view(), () => run(addRowAfterCommand.key));

  refresh = (v) => {
    if (!ready) return;
    const s = readState(v.state);
    for (const b of buttons) {
      const on = pressed(b.dataset.cmd ?? "", s);
      if (on === null) continue;
      b.setAttribute("aria-pressed", on ? "true" : "false");
    }
    tables.update(v);
    if (blockSelect) blockSelect.value = s.block;
    const kind = s.quote === "caution" || s.quote === "warning" || s.quote === "note" ? s.quote : "none";
    if (boxButton) {
      boxButton.dataset["kind"] = kind;
      const name = kind === "none" ? "" : (boxMenu.querySelector(`[data-box="${kind}"] span`)?.textContent ?? "");
      const title = name ? `${boxMenu.getAttribute("aria-label") ?? "Ruta"}: ${name}` : (boxMenu.getAttribute("aria-label") ?? "Ruta");
      boxButton.title = title;
      boxButton.setAttribute("aria-label", title);
    }
    for (const item of boxItems) {
      const box = item.dataset["box"];
      if (box === "none") item.disabled = s.quote === "";
      else item.setAttribute("aria-checked", box === kind ? "true" : "false");
    }
  };

  // --- Commands ---------------------------------------------------------------------------------
  const commands: Record<string, () => void> = {
    strong: () => run(toggleStrongCommand.key),
    emphasis: () => run(toggleEmphasisCommand.key),
    strike: () => run(toggleStrikethroughCommand.key),
    code: () => run(toggleInlineCodeCommand.key),
    bullet: () => toggleList("bullet_list"),
    ordered: () => toggleList("ordered_list"),
    box: () => (boxMenu.hidden ? openBoxMenu(false) : closeBoxMenu(false)),
    codeBlock: () => {
      if (readState(view().state).codeBlock) run(turnIntoTextCommand.key);
      else run(createCodeBlockCommand.key);
    },
    table: () => insertTable(),

    link: () => openLinkBar(),
    undo: () => run(undoCommand.key),
    redo: () => run(redoCommand.key),
  };


  /**
   * The Ruta menu: Fara / Varning / Info turn the current paragraph into that box or switch the box the cursor is in;
   * null ("Ingen ruta") turns the whole box back into normal text.
   */
  function setBox(kind: AlertKind | null): void {
    const v = view();
    const quote = nearest(v.state.selection.$from, ["blockquote"]);
    if (kind === null) {
      if (!quote) return;
      const range = v.state.doc.resolve(quote.pos + 1).blockRange(v.state.doc.resolve(quote.pos + quote.node.nodeSize - 1));
      const target = range ? liftTarget(range) : null;
      if (range && target !== null) v.dispatch(v.state.tr.lift(range, target));
      return;
    }
    if (quote) {
      v.dispatch(v.state.tr.setNodeMarkup(quote.pos, undefined, { ...quote.node.attrs, alert: kind }));
      return;
    }
    // Wrap the selected paragraphs in a box of this kind, in one step.
    const quoteType = v.state.schema.nodes["blockquote"];
    const { $from, $to } = v.state.selection;
    const range = $from.blockRange($to);
    const wrapping = range && quoteType ? findWrapping(range, quoteType, { alert: kind }) : null;
    if (range && wrapping) v.dispatch(v.state.tr.wrap(range, wrapping).scrollIntoView());
  }

  function toggleList(type: "bullet_list" | "ordered_list"): void {
    const v = view();
    const list = nearest(v.state.selection.$from, ["bullet_list", "ordered_list"]);
    if (list && list.node.type.name === type) {
      run(liftListItemCommand.key);
    } else if (list) {
      // Switch the kind of list in place instead of nesting a new one.
      const nodeType = v.state.schema.nodes[type];
      if (nodeType) v.dispatch(v.state.tr.setNodeMarkup(list.pos, nodeType));
    } else {
      run(type === "bullet_list" ? wrapInBulletListCommand.key : wrapInOrderedListCommand.key);
    }
  }

  // A 3 x 3 table with the cursor in its first cell. Unless the cursor is in an empty paragraph (which the table
  // replaces), it goes below the current top-level block, so it never lands inside a code block, list or quote.
  function insertTable(): void {
    const v = view();
    const { $from } = v.state.selection;
    const paragraph = v.state.schema.nodes["paragraph"];
    const inEmptyParagraph = $from.parent.type === paragraph && $from.parent.content.size === 0 && $from.depth === 1;
    if (paragraph && !inEmptyParagraph && $from.depth >= 1) {
      const at = $from.after(1);
      const tr = v.state.tr.insert(at, paragraph.create());
      v.dispatch(tr.setSelection(TextSelection.create(tr.doc, at + 1)));
    }
    run(insertTableCommand.key, { row: 3, col: 3 });
  }

  function setBlock(block: string): void {
    if (block === "h2") run(wrapInHeadingCommand.key, 2);
    else if (block === "h3") run(wrapInHeadingCommand.key, 3);
    else run(turnIntoTextCommand.key);
  }

  // --- Links ------------------------------------------------------------------------------------
  let linkRange: { from: number; to: number } | null = null;

  function openLinkBar(): void {
    const state = view().state;
    const linkType = state.schema.marks["link"];
    if (!linkType || !linkInput) return;
    const { from, to, empty } = state.selection;
    const around = empty ? markRange(state.selection.$from, linkType) : null;
    linkRange = around ?? { from, to };
    const existing = findLinkHref(state, linkRange.from, linkRange.to, linkType);
    linkInput.value = existing ?? "";
    if (linkError) linkError.hidden = true;
    linkBar!.hidden = false;
    linkInput.focus();
    linkInput.select();
  }

  function closeLinkBar(): void {
    linkBar!.hidden = true;
    linkRange = null;
    view().focus();
  }

  function saveLink(): void {
    const v = view();
    const linkType = v.state.schema.marks["link"];
    if (!linkType || !linkInput || !linkRange) return closeLinkBar();
    const href = linkInput.value.trim();
    if (!href) return removeLink();
    if (!isAllowedUrl(href)) {
      if (linkError) linkError.hidden = false;
      linkInput.focus();
      return;
    }
    const { from, to } = linkRange;
    let tr = v.state.tr;
    if (from === to) {
      // Nothing selected: the address itself becomes the link text.
      tr = tr.insertText(href, from).addMark(from, from + href.length, linkType.create({ href }));
    } else {
      tr = tr.removeMark(from, to, linkType).addMark(from, to, linkType.create({ href }));
    }
    v.dispatch(tr.scrollIntoView());
    closeLinkBar();
  }

  function removeLink(): void {
    const v = view();
    const linkType = v.state.schema.marks["link"];
    if (linkType && linkRange && linkRange.from !== linkRange.to) v.dispatch(v.state.tr.removeMark(linkRange.from, linkRange.to, linkType));
    closeLinkBar();
  }

  // --- DOM wiring -------------------------------------------------------------------------------
  const keepSelection = (e: MouseEvent): void => {
    // Clicking a toolbar button must not move focus (and the selection) out of the text.
    if ((e.target as Element).closest("button[data-cmd], .md-box-item")) e.preventDefault();
  };
  const onToolbarClick = (e: MouseEvent): void => {
    const button = (e.target as Element).closest<HTMLButtonElement>("button[data-cmd]");
    const command = button?.dataset.cmd ? commands[button.dataset.cmd] : undefined;
    if (!command) return;
    e.preventDefault();
    if (button?.dataset.cmd === "box") {
      // Opened with Enter or Space (no pointer: detail 0): focus goes into the menu, as for ArrowDown.
      if (boxMenu.hidden) openBoxMenu(e.detail === 0);
      else closeBoxMenu(e.detail === 0);
      return;
    }
    command();
    if (button?.dataset.cmd !== "link") view().focus();
  };
  const onBlockChange = (): void => {
    if (!blockSelect) return;
    setBlock(blockSelect.value);
    view().focus();
  };
  const onLinkBarClick = (e: MouseEvent): void => {
    const action = (e.target as Element).closest<HTMLElement>("[data-link]")?.dataset.link;
    if (action === "save") saveLink();
    else if (action === "remove") removeLink();
    else if (action === "cancel") closeLinkBar();
  };
  const onLinkKey = (e: KeyboardEvent): void => {
    if (e.key === "Enter") {
      e.preventDefault();
      saveLink();
    } else if (e.key === "Escape") {
      e.preventDefault();
      closeLinkBar();
    }
  };
  // Ctrl+K opens the link bar, as in Word.
  const onEditorKey = (e: KeyboardEvent): void => {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === "k") {
      e.preventDefault();
      openLinkBar();
    }
  };

  // The toolbar is one Tab stop (ARIA toolbar pattern): arrow keys, Home and End move between its controls.
  const controls = (): HTMLElement[] =>
    Array.from(toolbar.querySelectorAll<HTMLElement>("button[data-cmd], select[data-cmd]")).filter((c) => !(c as HTMLButtonElement).disabled);
  const focusControl = (target: HTMLElement): void => {
    for (const c of controls()) c.tabIndex = c === target ? 0 : -1;
    target.focus();
  };
  const onToolbarKey = (e: KeyboardEvent): void => {
    const list = controls();
    const i = list.indexOf(document.activeElement as HTMLElement);
    if (i < 0) return;
    let next: HTMLElement | undefined;
    if (e.key === "ArrowDown" && list[i] === boxButton) {
      e.preventDefault();
      openBoxMenu(true);
      return;
    }
    if (e.key === "ArrowRight") next = list[(i + 1) % list.length];
    else if (e.key === "ArrowLeft") next = list[(i - 1 + list.length) % list.length];
    else if (e.key === "Home") next = list[0];
    else if (e.key === "End") next = list[list.length - 1];
    if (!next) return;
    e.preventDefault();
    focusControl(next);
  };
  const onToolbarFocus = (e: FocusEvent): void => {
    // Clicking a control also makes it the toolbar's Tab stop.
    const target = e.target as HTMLElement;
    if (controls().includes(target)) for (const c of controls()) c.tabIndex = c === target ? 0 : -1;
  };
  // --- The Ruta menu -------------------------------------------------------------------------------
  function openBoxMenu(focusFirst: boolean): void {
    if (!boxButton) return;
    boxMenu.hidden = false;
    boxButton.setAttribute("aria-expanded", "true");
    placeBoxMenu();
    const items = enabledBoxItems();
    const current = items.find((i) => i.getAttribute("aria-checked") === "true");
    if (focusFirst) (current ?? items[0])?.focus();
  }
  function closeBoxMenu(returnFocus: boolean): void {
    if (boxMenu.hidden) return;
    boxMenu.hidden = true;
    boxButton?.setAttribute("aria-expanded", "false");
    if (returnFocus) boxButton?.focus();
  }
  const enabledBoxItems = (): HTMLButtonElement[] => boxItems.filter((i) => !i.disabled);
  /** Under the button, shifted to stay inside the editor; above it if there is no room below in the window. */
  function placeBoxMenu(): void {
    if (!boxButton) return;
    const holder = boxButton.parentElement as HTMLElement;
    const editorBox = root.getBoundingClientRect();
    const b = boxButton.getBoundingClientRect();
    boxMenu.style.left = "0px";
    boxMenu.style.top = `${b.height + 4}px`;
    boxMenu.style.bottom = "";
    const m = boxMenu.getBoundingClientRect();
    const shift = Math.min(0, editorBox.right - 8 - m.right) + Math.max(0, editorBox.left + 8 - m.left);
    boxMenu.style.left = `${shift}px`;
    if (m.bottom > window.innerHeight - 8 && b.top - m.height - 4 > 8) {
      boxMenu.style.top = "";
      boxMenu.style.bottom = `${holder.offsetHeight + 4}px`;
    }
  }
  const onBoxMenuClick = (e: MouseEvent): void => {
    const item = (e.target as Element).closest<HTMLButtonElement>(".md-box-item");
    if (!item || item.disabled) return;
    e.preventDefault();
    const box = item.dataset["box"];
    setBox(box === "caution" || box === "warning" || box === "note" ? box : null);
    closeBoxMenu(false);
    view().focus();
  };
  const onBoxMenuKey = (e: KeyboardEvent): void => {
    const items = enabledBoxItems();
    const i = items.indexOf(document.activeElement as HTMLButtonElement);
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      e.stopPropagation();
      const step = e.key === "ArrowDown" ? 1 : -1;
      items[(i + step + items.length) % items.length]?.focus();
    } else if (e.key === "Home" || e.key === "End") {
      e.preventDefault();
      e.stopPropagation();
      (e.key === "Home" ? items[0] : items[items.length - 1])?.focus();
    } else if (e.key === "Escape") {
      e.preventDefault();
      e.stopPropagation();
      closeBoxMenu(true);
    } else if (e.key === "Tab") {
      closeBoxMenu(false);
    }
  };
  const onDocPointer = (e: Event): void => {
    if (!boxMenu.hidden && !(e.target as Element).closest(".md-box")) closeBoxMenu(false);
  };
  // Escape closes the menu also when it was opened with the mouse and focus stayed in the text.
  const onDocKey = (e: KeyboardEvent): void => {
    if (e.key !== "Escape" || boxMenu.hidden || boxMenu.contains(document.activeElement)) return;
    e.preventDefault();
    closeBoxMenu(false);
  };
  boxMenu.addEventListener("click", onBoxMenuClick);
  boxMenu.addEventListener("keydown", onBoxMenuKey);
  document.addEventListener("pointerdown", onDocPointer, true);
  document.addEventListener("keydown", onDocKey, true);
  toolbar.addEventListener("keydown", onToolbarKey);
  toolbar.addEventListener("focusin", onToolbarFocus);
  toolbar.addEventListener("mousedown", keepSelection);
  toolbar.addEventListener("click", onToolbarClick);
  blockSelect?.addEventListener("change", onBlockChange);
  linkBar.addEventListener("click", onLinkBarClick);
  linkInput?.addEventListener("keydown", onLinkKey);
  // Tab in a table's last cell adds a row (Milkdown alone would move focus out of the editor). Capture phase, so
  // this runs before ProseMirror's own Tab handling.
  const onTableTab = (e: KeyboardEvent): void => {
    if (tables.handleKey(e)) {
      e.preventDefault();
      e.stopPropagation();
      return;
    }
    if (e.key !== "Tab" || e.shiftKey || e.ctrlKey || e.altKey || e.metaKey) return;
    const v = view();
    if (!readTable(v.state)) return;
    e.preventDefault();
    e.stopPropagation();
    nextCellOrNewRow(v.state, v.dispatch, () => run(addRowAfterCommand.key), () => view().state);
  };

  surface.addEventListener("keydown", onEditorKey);
  surface.addEventListener("keydown", onTableTab, true);
  for (const b of buttons) b.disabled = false;
  if (blockSelect) blockSelect.disabled = false;
  // One Tab stop: the first control (the block type) until another one is used.
  controls().forEach((c, i) => (c.tabIndex = i === 0 ? 0 : -1));
  ready = true;
  refresh(view());

  return {
    editor,
    dispose: () => {
      toolbar.removeEventListener("mousedown", keepSelection);
      toolbar.removeEventListener("keydown", onToolbarKey);
      boxMenu.removeEventListener("click", onBoxMenuClick);
      boxMenu.removeEventListener("keydown", onBoxMenuKey);
      document.removeEventListener("pointerdown", onDocPointer, true);
      document.removeEventListener("keydown", onDocKey, true);
      toolbar.removeEventListener("focusin", onToolbarFocus);
      toolbar.removeEventListener("click", onToolbarClick);
      blockSelect?.removeEventListener("change", onBlockChange);
      linkBar.removeEventListener("click", onLinkBarClick);
      linkInput?.removeEventListener("keydown", onLinkKey);
      surface.removeEventListener("keydown", onEditorKey);
      surface.removeEventListener("keydown", onTableTab, true);
      tables.destroy();
      void editor.destroy();
    },
  };
}

/** The document as Markdown, without trailing blank lines. */
/** The document as Markdown (links and images with a refused address are removed first). */
export function getMarkdown(handle: MarkdownEditorHandle): string {
  return markdownOf(handle.editor);
}

export function destroy(handle: MarkdownEditorHandle): void {
  handle.dispose();
}

// --- State helpers ------------------------------------------------------------------------------

function readState(state: EditorState): ToolbarState {
  const { $from, from, to, empty } = state.selection;
  const has = (name: string): boolean => {
    const type = state.schema.marks[name];
    if (!type) return false;
    if (empty) return !!type.isInSet(state.storedMarks ?? $from.marks());
    return state.doc.rangeHasMark(from, to, type);
  };
  const parent = $from.parent;
  let block: ToolbarState["block"] = "";
  if (parent.type.name === "paragraph") block = "p";
  else if (parent.type.name === "heading") block = parent.attrs["level"] === 2 ? "h2" : parent.attrs["level"] === 3 ? "h3" : "";
  const list = nearest($from, ["bullet_list", "ordered_list"]);
  return {
    block,
    marks: {
      strong: has("strong"),
      emphasis: has("emphasis"),
      strike_through: has("strike_through"),
      inlineCode: has("inlineCode"),
      link: has("link"),
    },
    bullet: list?.node.type.name === "bullet_list",
    ordered: list?.node.type.name === "ordered_list",
    quote: quoteKind($from),
    codeBlock: parent.type.name === "code_block",
  };
}

function quoteKind($pos: ResolvedPos): AlertKind | "plain" | "" {
  const quote = nearest($pos, ["blockquote"]);
  if (!quote) return "";
  const alert = quote.node.attrs["alert"];
  return alert === "caution" || alert === "warning" || alert === "note" ? alert : "plain";
}

/** Whether a toolbar button shows as pressed; null for buttons that are actions, not states. */
function pressed(cmd: string, s: ToolbarState): boolean | null {
  switch (cmd) {
    case "strong": return s.marks.strong;
    case "emphasis": return s.marks.emphasis;
    case "strike": return s.marks.strike_through;
    case "code": return s.marks.inlineCode;
    case "link": return s.marks.link;
    case "bullet": return s.bullet;
    case "ordered": return s.ordered;
    case "codeBlock": return s.codeBlock;
    default: return null;
  }
}

function nearest($pos: ResolvedPos, names: string[]): { node: ProseNode; pos: number } | null {
  for (let d = $pos.depth; d > 0; d--) {
    const node = $pos.node(d);
    if (names.includes(node.type.name)) return { node, pos: $pos.before(d) };
  }
  return null;
}

/** The extent of the mark of this type around a cursor position, within its text block. */
function markRange($pos: ResolvedPos, type: MarkType): { from: number; to: number } | null {
  const parent = $pos.parent;
  const start = $pos.start();
  const index = $pos.index();
  const here = parent.maybeChild(index) ?? parent.maybeChild(index - 1);
  const mark = here ? type.isInSet(here.marks) : undefined;
  if (!mark) return null;
  // Contiguous runs of children carrying this exact mark; pick the run around the cursor.
  const runs: { from: number; to: number }[] = [];
  let pos = start;
  parent.forEach((child) => {
    const childFrom = pos;
    pos += child.nodeSize;
    if (!mark.isInSet(child.marks)) return;
    const last = runs[runs.length - 1];
    if (last && last.to === childFrom) last.to = pos;
    else runs.push({ from: childFrom, to: pos });
  });
  return runs.find((r) => r.from <= $pos.pos && $pos.pos <= r.to) ?? null;
}

function findLinkHref(state: EditorState, from: number, to: number, type: MarkType): string | null {
  let href: string | null = null;
  state.doc.nodesBetween(from, Math.max(to, from + 1), (node) => {
    const mark = type.isInSet(node.marks);
    if (mark && href === null) href = String(mark.attrs["href"] ?? "");
  });
  return href;
}
