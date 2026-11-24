// Table controls for the åtgärd editor (design: design-preview/milkdown/design/table-controls-mockup.html, option A).
// Only the current cell gets controls: the hovered cell with a mouse, otherwise the cell with the cursor (so touch
// screens work by tapping a cell). Around it:
//   - a grip left of its row and a grip above its column: drag to move (mouse at once, touch after a 330 ms hold),
//     tap or click for a menu (row: Flytta upp/ner, Ta bort rad, Ta bort tabell; column: alignment, Flytta åt
//     vänster/höger, Ta bort kolumn);
//   - a + under the row ("Lägg till rad under") and a + right of the column ("Lägg till kolumn till höger").
// One way per action, except moving, which also sits in the grip's menu for touch and keyboard users.
// Everything is drawn in an overlay next to ProseMirror's DOM, never inside it.
import type { EditorView } from "@milkdown/kit/prose/view";
import { TextSelection, type EditorState, type Transaction } from "@milkdown/kit/prose/state";
import { addColumnAfter, findTable } from "@milkdown/kit/prose/tables";
import {
  alignColumns,
  canMoveColumn,
  canMoveRow,
  canRemoveColumn,
  canRemoveRow,
  moveColumn,
  moveRow,
  removeColumns,
  removeRows,
  removeTable,
  autoScrollStep,
  type Align,
} from "./table-tools";

const sv = {
  rowGrip: "Flytta rad: håll och dra, eller Alt+pil upp/ner. Tryck för fler val (Alt+Enter).",
  headerGrip: "Rubrikraden ligger alltid först. Tryck för fler val (Alt+Enter).",
  colGrip: "Flytta kolumn: håll och dra, eller Alt+pil vänster/höger. Tryck för fler val (Alt+Skift+Enter).",
  addRow: "Lägg till rad under",
  addCol: "Lägg till kolumn till höger",
  menuRow: "Rad",
  menuCol: "Kolumn",
  up: "Flytta upp",
  down: "Flytta ner",
  left: "Flytta åt vänster",
  right: "Flytta åt höger",
  alignLeft: "Vänsterjustera",
  alignCenter: "Centrera",
  alignRight: "Högerjustera",
  deleteRow: "Ta bort rad",
  deleteCol: "Ta bort kolumn",
  deleteTable: "Ta bort tabell",
  confirm: "Är du säker? Ta bort",
  cancel: "Avbryt",
  headerFixed: "Rubrikraden ligger alltid först",
  firstBody: "Raden ligger redan först under rubrikraden",
  lastRow: "Raden ligger redan sist",
  headerKeep: "Rubrikraden kan inte tas bort",
  oneRow: "Tabellen måste ha minst en rad under rubrikraden",
  firstCol: "Kolumnen ligger redan först",
  lastCol: "Kolumnen ligger redan sist",
  oneCol: "Tabellen måste ha minst en kolumn",
  dropHere: "Släpp för att flytta hit",
};

const icons = {
  grip: '<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="5" cy="3" r="1.5"/><circle cx="11" cy="3" r="1.5"/><circle cx="5" cy="8" r="1.5"/><circle cx="11" cy="8" r="1.5"/><circle cx="5" cy="13" r="1.5"/><circle cx="11" cy="13" r="1.5"/></svg>',
  gripH: '<svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="3" cy="5" r="1.5"/><circle cx="3" cy="11" r="1.5"/><circle cx="8" cy="5" r="1.5"/><circle cx="8" cy="11" r="1.5"/><circle cx="13" cy="5" r="1.5"/><circle cx="13" cy="11" r="1.5"/></svg>',
  plus: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>',
  up: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 19V5M6 11l6-6 6 6"/></svg>',
  down: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M6 13l6 6 6-6"/></svg>',
  left: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M19 12H5M11 6l-6 6 6 6"/></svg>',
  right: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14M13 6l6 6-6 6"/></svg>',
  trash: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/></svg>',
  table: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 10h18M9 4v16"/></svg>',
  alignLeft: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 10h10M4 14h16M4 18h10"/></svg>',
  alignCenter: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M7 10h10M4 14h16M7 18h10"/></svg>',
  alignRight: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M10 10h10M4 14h16M10 18h10"/></svg>',
};

/** Grips sit this far outside the table (centre); the editor keeps 36px free around every table. */
const GRIP_OFFSET = 18;
/** The pointer may go this far outside the table, towards its controls, and still count as at the table. */
const REACH = 48;
const HOVER_LEAVE_MS = 150;
const LONG_PRESS_MS = 330;
const DRAG_THRESHOLD = 4;
/** Auto-scroll while dragging a row only starts this close to the visible top or bottom edge, and only if part of
 *  the table is hidden beyond that edge. */
const AUTOSCROLL_EDGE = 40;
const AUTOSCROLL_MAX_SPEED = 6; // px per frame

type Command = (state: EditorState, dispatch: (tr: Transaction) => void) => boolean;
type Axis = "row" | "col";

interface At {
  table: HTMLTableElement;
  cell: HTMLTableCellElement;
}

interface MenuItem {
  icon: string;
  label: string;
  run: () => void;
  disabled?: string; // tooltip saying why
  danger?: boolean;
  checked?: boolean;
  separatorBefore?: boolean;
  confirm?: boolean; // ask "Är du säker?" inside the menu first
}

interface Drag {
  axis: Axis;
  at: At;
  from: number;
  slot: number | null; // insert before this row/column index; null = no change
  x: number;
  y: number;
  ghost: HTMLElement;
  shade: HTMLElement;
  line: HTMLElement;
  frame: number;
}

export interface TableOverlay {
  update: (view: EditorView) => void;
  /** Alt+arrows move the current row or column, Alt+Enter / Alt+Shift+Enter open the row / column menu. */
  handleKey: (e: KeyboardEvent) => boolean;
  destroy: () => void;
}

/**
 * Overlay for the editor's tables. `rowAfter` is Milkdown's own "add row" command: it creates a body row (never a
 * second header row) and keeps the column alignment. `toolbar` is the editor's sticky toolbar, which menus and
 * auto-scroll keep clear of.
 */
export function createTableOverlay(surface: HTMLElement, toolbar: HTMLElement, getView: () => EditorView, rowAfter: () => void): TableOverlay {
  const layer = document.createElement("div");
  layer.className = "md-tbl-layer";
  surface.appendChild(layer);

  let hover: At | null = null;
  let hoverTimer = 0;
  let menu: { el: HTMLElement; at: At; axis: Axis } | null = null;
  let drag: Drag | null = null;
  /** A pointer is down on a grip: don't rebuild the controls under it. */
  let gesture = false;

  // --- Where are we -----------------------------------------------------------------------------
  const selectionAt = (view: EditorView): At | null => {
    const found = findTable(view.state.selection.$from);
    if (!found) return null;
    const dom = view.nodeDOM(found.pos);
    const table = dom instanceof HTMLTableElement ? dom : dom instanceof HTMLElement ? dom.querySelector("table") : null;
    if (!table) return null;
    const { node, offset } = view.domAtPos(view.state.selection.from);
    const el = node instanceof Element ? (node.childNodes[offset] instanceof Element ? (node.childNodes[offset] as Element) : node) : node.parentElement;
    const cell = el?.closest<HTMLTableCellElement>("td, th");
    return cell && table.contains(cell) ? { table, cell } : null;
  };

  const current = (): At | null => {
    const at = drag?.at ?? menu?.at ?? hover ?? selectionAt(getView());
    return at && at.table.isConnected && at.cell.isConnected ? at : null;
  };

  const indexes = (at: At) => {
    const rows = Array.from(at.table.rows);
    const row = at.cell.parentElement as HTMLTableRowElement;
    return { rows, r: rows.indexOf(row), c: Array.from(row.cells).indexOf(at.cell), columns: rows[0]?.cells.length ?? 0 };
  };

  /** Puts the cursor in a cell, then runs a table command there. */
  const inCell = (cell: HTMLTableCellElement, command: Command | (() => void)): void => {
    const view = getView();
    const pos = view.posAtDOM(cell, 0);
    view.dispatch(view.state.tr.setSelection(TextSelection.near(view.state.doc.resolve(pos))));
    if (command.length === 0) (command as () => void)();
    else (command as Command)(getView().state, getView().dispatch);
    closeMenu();
    hover = null;
    getView().focus();
  };

  // --- Drawing ------------------------------------------------------------------------------------
  const el = (cls: string, x: number, y: number, w?: number, h?: number): HTMLElement => {
    const d = document.createElement("div");
    d.className = cls;
    d.style.left = `${x}px`;
    d.style.top = `${y}px`;
    if (w !== undefined) d.style.width = `${w}px`;
    if (h !== undefined) d.style.height = `${h}px`;
    layer.appendChild(d);
    return d;
  };

  const control = (cls: string, title: string, icon: string, x: number, y: number): HTMLButtonElement => {
    const b = document.createElement("button");
    b.type = "button";
    b.tabIndex = -1; // not in the Tab order: Tab moves between cells
    b.className = `md-tbl-ctl ${cls}`;
    b.title = title;
    b.setAttribute("aria-label", title);
    b.innerHTML = `<span class="md-tbl-v">${icon}</span>`;
    b.style.left = `${x}px`;
    b.style.top = `${y}px`;
    layer.appendChild(b);
    return b;
  };

  /** Removes the controls, except `keep` (the grip being dragged must stay, or the browser drops the gesture). */
  function clear(keep?: Element): void {
    layer.querySelectorAll(".md-tbl-ctl, .md-tbl-band, .md-tbl-cur").forEach((e) => {
      if (e !== keep) e.remove();
    });
  }

  function render(): void {
    if (gesture || drag) return;
    clear();
    const at = current();
    if (!at) {
      closeMenu();
      return;
    }
    const { rows, r, c, columns } = indexes(at);
    const row = rows[r];
    const headerCell = rows[0]?.cells[c];
    if (!row || r < 0 || c < 0 || !headerCell) return;

    const s = surface.getBoundingClientRect();
    const t = at.table.getBoundingClientRect();
    const rr = row.getBoundingClientRect();
    const cr = at.cell.getBoundingClientRect();
    const hr = headerCell.getBoundingClientRect();
    const x = (v: number) => v - s.left;
    const y = (v: number) => v - s.top;

    // Faint tint on the current row and column, outline on the cell.
    el("md-tbl-band", x(t.left), y(rr.top), t.width, rr.height);
    el("md-tbl-band", x(cr.left), y(t.top), cr.width, t.height);
    el("md-tbl-cur", x(cr.left), y(cr.top), cr.width, cr.height);

    const isHeader = r === 0;
    const rowGrip = control("md-tbl-grip md-tbl-grip-row" + (menu?.axis === "row" ? " is-open" : ""), isHeader ? sv.headerGrip : sv.rowGrip, icons.grip, x(t.left) - GRIP_OFFSET, y(rr.top) + rr.height / 2);
    const colGrip = control("md-tbl-grip md-tbl-grip-col" + (menu?.axis === "col" ? " is-open" : ""), sv.colGrip, icons.gripH, x(hr.left) + hr.width / 2, y(t.top) - GRIP_OFFSET);
    wireGrip(rowGrip, "row", at, r, !isHeader, () => openRowMenu(at, rowGrip));
    wireGrip(colGrip, "col", at, c, columns > 1, () => openColumnMenu(at, colGrip));

    const firstCell = row.cells[0] ?? at.cell;
    const addRow = control("md-tbl-plus", sv.addRow, icons.plus, x(t.left), y(rr.bottom));
    addRow.addEventListener("click", () => inCell(firstCell, rowAfter));
    const addCol = control("md-tbl-plus", sv.addCol, icons.plus, x(hr.right), y(t.top));
    addCol.addEventListener("click", () => inCell(headerCell, addColumnAfter));

    if (menu) placeMenu(menu.el, menu.axis === "row" ? rowGrip : colGrip);
  }

  // --- Grips: tap for the menu, drag to move ------------------------------------------------------
  function wireGrip(grip: HTMLButtonElement, axis: Axis, at: At, index: number, movable: boolean, openMenu: () => void): void {
    grip.addEventListener("pointerdown", (e) => {
      if (e.button !== 0) return;
      e.preventDefault(); // keep the cursor in the text
      gesture = true;
      const pointerId = e.pointerId;
      const startX = e.clientX;
      const startY = e.clientY;
      const touch = e.pointerType !== "mouse";
      let dragging = false;
      let cancelled = false;
      let timer = 0;
      if (touch && movable) {
        timer = window.setTimeout(() => {
          dragging = true;
          navigator.vibrate?.(15);
          startDrag(axis, at, index, startX, startY, grip);
        }, LONG_PRESS_MS);
      }
      // Listen on the window: the pointer leaves the small grip as soon as it moves.
      const onMoveGrip = (ev: PointerEvent): void => {
        if (ev.pointerId !== pointerId) return;
        const dist = Math.hypot(ev.clientX - startX, ev.clientY - startY);
        if (!dragging) {
          if (touch && dist > 10) {
            // A swipe, not a hold: neither drag nor menu.
            window.clearTimeout(timer);
            cancelled = true;
          } else if (!touch && movable && dist > DRAG_THRESHOLD) {
            dragging = true;
            startDrag(axis, at, index, ev.clientX, ev.clientY, grip);
          }
        }
        if (dragging) {
          ev.preventDefault(); // no text selection or native panning while dragging
          updateDrag(ev.clientX, ev.clientY);
        }
      };
      const end = (ev: PointerEvent): void => {
        if (ev.pointerId !== pointerId) return;
        window.clearTimeout(timer);
        window.removeEventListener("pointermove", onMoveGrip, true);
        window.removeEventListener("pointerup", end, true);
        window.removeEventListener("pointercancel", end, true);
        gesture = false;
        if (dragging) {
          if (ev.type === "pointerup") finishDrag();
          else cancelDrag();
        } else if (!cancelled && ev.type === "pointerup") {
          if (menu?.axis === axis) {
            closeMenu();
            render();
          } else openMenu();
        } else render();
      };
      window.addEventListener("pointermove", onMoveGrip, true);
      window.addEventListener("pointerup", end, true);
      window.addEventListener("pointercancel", end, true);
    });
  }

  function startDrag(axis: Axis, at: At, from: number, x: number, y: number, grip: HTMLElement): void {
    closeMenu();
    clear(grip);
    grip.classList.add("is-open");
    const s = surface.getBoundingClientRect();
    const t = at.table.getBoundingClientRect();
    const { rows } = indexes(at);
    // The ghost: a copy of the row or column's text, following the pointer on its axis.
    const ghost = el(`md-tbl-ghost md-tbl-ghost-${axis}`, 0, 0);
    let shadeRect: DOMRect;
    if (axis === "row") {
      const row = rows[from]!;
      shadeRect = row.getBoundingClientRect();
      for (const cell of Array.from(row.cells)) {
        const d = document.createElement("div");
        d.textContent = cell.textContent ?? "";
        d.style.width = `${cell.getBoundingClientRect().width}px`;
        d.style.textAlign = cell.style.textAlign;
        ghost.appendChild(d);
      }
      ghost.style.width = `${t.width}px`;
      ghost.style.height = `${shadeRect.height}px`;
      ghost.style.left = `${t.left - s.left}px`;
    } else {
      const head = rows[0]!.cells[from]!.getBoundingClientRect();
      shadeRect = new DOMRect(head.left, t.top, head.width, t.height);
      for (const row of rows) {
        const cell = row.cells[from];
        const d = document.createElement("div");
        d.textContent = cell?.textContent ?? "";
        d.style.height = `${cell?.getBoundingClientRect().height ?? 0}px`;
        ghost.appendChild(d);
      }
      ghost.style.width = `${head.width}px`;
      ghost.style.height = `${t.height}px`;
      ghost.style.top = `${t.top - s.top}px`;
    }
    // The source fades: a veil over it in the editor's background colour.
    const shade = el("md-tbl-shade", shadeRect.left - s.left, shadeRect.top - s.top, shadeRect.width, shadeRect.height);
    const line = el(`md-tbl-drop md-tbl-drop-${axis}`, 0, 0);
    line.title = sv.dropHere;
    drag = { axis, at, from, slot: null, x, y, ghost, shade, line, frame: 0 };
    document.body.classList.add("md-tbl-dragging");
    updateDrag(x, y);
    drag.frame = requestAnimationFrame(autoScroll);
  }

  /** Slots are the gaps a row or column can be dropped into; for rows never above the header (slot 0). */
  function slots(d: Drag): { index: number; at: number }[] {
    const { rows } = indexes(d.at);
    if (d.axis === "row") {
      const out = rows.slice(1).map((row, i) => ({ index: i + 1, at: row.getBoundingClientRect().top }));
      const last = rows[rows.length - 1];
      if (last) out.push({ index: rows.length, at: last.getBoundingClientRect().bottom });
      return out;
    }
    const head = Array.from(rows[0]?.cells ?? []);
    const out = head.map((cell, i) => ({ index: i, at: cell.getBoundingClientRect().left }));
    const lastCell = head[head.length - 1];
    if (lastCell) out.push({ index: head.length, at: lastCell.getBoundingClientRect().right });
    return out;
  }

  /** The row/column index the moved one ends up at when dropped into `slot`. */
  const finalIndex = (from: number, slot: number) => (slot > from ? slot - 1 : slot);

  function updateDrag(x: number, y: number): void {
    const d = drag;
    if (!d) return;
    d.x = x;
    d.y = y;
    const s = surface.getBoundingClientRect();
    const t = d.at.table.getBoundingClientRect();
    const pos = d.axis === "row" ? y : x;
    let best: { index: number; at: number } | null = null;
    for (const slot of slots(d)) if (!best || Math.abs(slot.at - pos) < Math.abs(best.at - pos)) best = slot;
    const { rows } = indexes(d.at);
    const count = d.axis === "row" ? rows.length : (rows[0]?.cells.length ?? 0);
    const to = best ? finalIndex(d.from, best.index) : d.from;
    const allowed = d.axis === "row" ? canMoveRow(count, d.from, to) : canMoveColumn(count, d.from, to);
    d.slot = best && allowed ? best.index : null;
    if (d.axis === "row") {
      d.ghost.style.top = `${Math.max(t.top, Math.min(t.bottom - d.ghost.offsetHeight, y - d.ghost.offsetHeight / 2)) - s.top}px`;
      d.ghost.style.left = `${t.left - s.left}px`;
      d.shade.style.top = `${rows[d.from]!.getBoundingClientRect().top - s.top}px`;
    } else {
      d.ghost.style.left = `${Math.max(t.left, Math.min(t.right - d.ghost.offsetWidth, x - d.ghost.offsetWidth / 2)) - s.left}px`;
      d.ghost.style.top = `${t.top - s.top}px`;
      d.shade.style.top = `${t.top - s.top}px`;
    }
    d.line.hidden = d.slot === null;
    if (best && d.slot !== null) {
      if (d.axis === "row") {
        d.line.style.left = `${t.left - s.left}px`;
        d.line.style.width = `${t.width}px`;
        d.line.style.top = `${best.at - s.top}px`;
      } else {
        d.line.style.top = `${t.top - s.top}px`;
        d.line.style.height = `${t.height}px`;
        d.line.style.left = `${best.at - s.left}px`;
      }
    }
  }

  /** The nearest ancestor that scrolls vertically; null means the page itself. */
  function scroller(): HTMLElement | null {
    for (let e = surface.parentElement; e && e !== document.body && e !== document.documentElement; e = e.parentElement) {
      const overflow = getComputedStyle(e).overflowY;
      if ((overflow === "auto" || overflow === "scroll") && e.scrollHeight > e.clientHeight) return e;
    }
    return null;
  }

  /**
   * While a ROW is dragged near the visible top or bottom edge, scrolls gently so rows beyond it can be reached.
   * Only when the table actually continues past that edge: a table that is fully visible never scrolls the page.
   * The visible top is under the sticky toolbar only while the toolbar is stuck; column drags never scroll.
   */
  function autoScroll(): void {
    const d = drag;
    if (!d) return;
    d.frame = requestAnimationFrame(autoScroll);
    if (d.axis !== "row") return;
    const box = scroller();
    const area = box ? box.getBoundingClientRect() : new DOMRect(0, 0, window.innerWidth, window.innerHeight);
    const tb = toolbar.getBoundingClientRect();
    const stuck = tb.top <= area.top + 1 && tb.bottom > area.top;
    const top = stuck ? tb.bottom : area.top;
    const bottom = area.bottom;
    const t = d.at.table.getBoundingClientRect();
    const dy = autoScrollStep(d.axis, d.y, top, bottom, t.top, t.bottom, AUTOSCROLL_EDGE, AUTOSCROLL_MAX_SPEED);
    if (dy === 0) return;
    if (box) box.scrollTop += dy;
    else window.scrollBy(0, dy);
    updateDrag(d.x, d.y);
  }

  function endDrag(): Drag | null {
    const d = drag;
    if (!d) return null;
    cancelAnimationFrame(d.frame);
    d.ghost.remove();
    d.shade.remove();
    d.line.remove();
    drag = null;
    document.body.classList.remove("md-tbl-dragging");
    return d;
  }

  function finishDrag(): void {
    const d = endDrag();
    if (!d || d.slot === null) return render();
    const to = finalIndex(d.from, d.slot);
    const { rows } = indexes(d.at);
    const source = d.axis === "row" ? rows[d.from]?.cells[0] : rows[0]?.cells[d.from];
    if (!source) return render();
    inCell(source, (st, disp) => (d.axis === "row" ? moveRow(st, disp, d.from, to) : moveColumn(st, disp, d.from, to)));
  }

  function cancelDrag(): void {
    endDrag();
    render();
  }

  // --- Menus --------------------------------------------------------------------------------------
  function openRowMenu(at: At, anchor: HTMLElement): void {
    const { rows, r } = indexes(at);
    const first = at.cell.parentElement && (at.cell.parentElement as HTMLTableRowElement).cells[0];
    if (!first) return;
    const header = r === 0;
    const move = (to: number) => () => inCell(first, (s, d) => moveRow(s, d, r, to));
    openMenu(at, "row", anchor, sv.menuRow, [
      { icon: icons.up, label: sv.up, run: move(r - 1), ...(header ? { disabled: sv.headerFixed } : r === 1 ? { disabled: sv.firstBody } : {}) },
      { icon: icons.down, label: sv.down, run: move(r + 1), ...(header ? { disabled: sv.headerFixed } : r >= rows.length - 1 ? { disabled: sv.lastRow } : {}) },
      {
        icon: icons.trash,
        label: sv.deleteRow,
        danger: true,
        separatorBefore: true,
        run: () => inCell(first, removeRows),
        ...(header ? { disabled: sv.headerKeep } : !canRemoveRow(rows.length, r) ? { disabled: sv.oneRow } : {}),
      },
      { icon: icons.table, label: sv.deleteTable, danger: true, confirm: true, run: () => inCell(first, removeTable) },
    ]);
  }

  function openColumnMenu(at: At, anchor: HTMLElement): void {
    const { rows, c, columns } = indexes(at);
    const head = rows[0]?.cells[c];
    if (!head) return;
    const align = (["left", "center", "right"] as Align[]).find((a) => head.style.textAlign === a) ?? "left";
    const alignTo = (to: Align) => () => inCell(head, (s, d) => alignColumns(s, d, to));
    const move = (to: number) => () => inCell(head, (s, d) => moveColumn(s, d, c, to));
    openMenu(at, "col", anchor, sv.menuCol, [
      { icon: icons.alignLeft, label: sv.alignLeft, checked: align === "left", run: alignTo("left") },
      { icon: icons.alignCenter, label: sv.alignCenter, checked: align === "center", run: alignTo("center") },
      { icon: icons.alignRight, label: sv.alignRight, checked: align === "right", run: alignTo("right") },
      { icon: icons.left, label: sv.left, separatorBefore: true, run: move(c - 1), ...(c === 0 ? { disabled: sv.firstCol } : {}) },
      { icon: icons.right, label: sv.right, run: move(c + 1), ...(c >= columns - 1 ? { disabled: sv.lastCol } : {}) },
      {
        icon: icons.trash,
        label: sv.deleteCol,
        danger: true,
        separatorBefore: true,
        run: () => inCell(head, removeColumns),
        ...(!canRemoveColumn(columns) ? { disabled: sv.oneCol } : {}),
      },
    ]);
  }

  function closeMenu(): void {
    menu?.el.remove();
    menu = null;
  }

  function openMenu(at: At, axis: Axis, anchor: HTMLElement, title: string, items: MenuItem[]): void {
    closeMenu();
    const m = document.createElement("div");
    m.className = "md-tbl-menu";
    m.setAttribute("role", "menu");
    m.setAttribute("aria-label", title);
    for (const item of items) {
      if (item.separatorBefore) m.appendChild(Object.assign(document.createElement("hr"), { className: "md-tbl-sep" }));
      const b = menuButton(item.icon, item.label, item.danger);
      if (item.checked) {
        b.setAttribute("aria-checked", "true");
        b.setAttribute("role", "menuitemradio");
        b.insertAdjacentHTML("beforeend", '<span class="md-tbl-check" aria-hidden="true">✓</span>');
      }
      if (item.disabled) {
        b.disabled = true;
        b.title = item.disabled;
      }
      b.addEventListener("click", (e) => {
        e.preventDefault();
        if (item.confirm) askFirst(m, b, item.run);
        else item.run();
      });
      m.appendChild(b);
    }
    m.addEventListener("keydown", (e) => {
      if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
      e.preventDefault();
      const enabled = Array.from(m.querySelectorAll<HTMLButtonElement>("button:not(:disabled)"));
      const i = enabled.indexOf(document.activeElement as HTMLButtonElement);
      const next = enabled[(i + (e.key === "ArrowDown" ? 1 : -1) + enabled.length) % enabled.length];
      next?.focus();
    });
    layer.appendChild(m);
    menu = { el: m, at, axis };
    anchor.classList.add("is-open");
    placeMenu(m, anchor);
    m.querySelector<HTMLButtonElement>("button:not(:disabled)")?.focus({ preventScroll: true });
  }

  function menuButton(icon: string, label: string, danger = false): HTMLButtonElement {
    const b = document.createElement("button");
    b.type = "button";
    b.setAttribute("role", "menuitem");
    b.className = "md-tbl-item" + (danger ? " md-tbl-danger" : "");
    b.innerHTML = `${icon}<span>${label}</span>`;
    return b;
  }

  /** "Ta bort tabell": the item turns into "Är du säker? Ta bort" plus "Avbryt", right in the menu. */
  function askFirst(m: HTMLElement, item: HTMLButtonElement, run: () => void): void {
    const yes = menuButton(icons.table, sv.confirm, true);
    yes.classList.add("md-tbl-confirm");
    yes.addEventListener("click", (e) => {
      e.preventDefault();
      run();
    });
    const no = menuButton("", sv.cancel);
    no.addEventListener("click", (e) => {
      e.preventDefault();
      yes.replaceWith(item);
      no.remove();
      item.focus({ preventScroll: true });
    });
    item.replaceWith(yes);
    yes.after(no);
    yes.focus({ preventScroll: true });
    if (menu) placeMenu(m, layer.querySelector<HTMLElement>(".md-tbl-grip.is-open") ?? item);
  }

  /** Below the grip, or above it if it doesn't fit; shifted sideways to stay at least 8px inside the editor. */
  function placeMenu(m: HTMLElement, anchor: HTMLElement): void {
    const s = surface.getBoundingClientRect();
    const a = anchor.getBoundingClientRect();
    const width = m.offsetWidth;
    const height = m.offsetHeight;
    let left = a.left - s.left;
    left = Math.max(8, Math.min(left, surface.clientWidth - width - 8));
    const visibleTop = Math.max(toolbar.getBoundingClientRect().bottom, 0);
    const below = a.bottom + 4;
    const above = a.top - 4 - height;
    const fitsBelow = below + height <= Math.min(window.innerHeight, s.bottom + 400) - 8;
    const fitsAbove = above >= visibleTop + 8;
    const top = !fitsBelow && fitsAbove ? above : below;
    m.style.left = `${left}px`;
    m.style.top = `${top - s.top}px`;
  }

  // --- Pointer, outside clicks, keys, layout ---------------------------------------------------------
  const onMove = (e: MouseEvent): void => {
    if (gesture || drag) return;
    const target = e.target as Element;
    if (target.closest(".md-tbl-ctl, .md-tbl-menu")) {
      window.clearTimeout(hoverTimer);
      return;
    }
    const cell = target.closest<HTMLTableCellElement>(".ProseMirror td, .ProseMirror th");
    const table = cell?.closest("table");
    if (cell && table) {
      window.clearTimeout(hoverTimer);
      if (hover?.cell !== cell) {
        hover = { table, cell };
        render();
      }
      return;
    }
    if (!hover) return;
    const t = hover.table.getBoundingClientRect();
    const near = e.clientX > t.left - REACH && e.clientX < t.right + REACH && e.clientY > t.top - REACH && e.clientY < t.bottom + REACH;
    if (near) {
      window.clearTimeout(hoverTimer);
      return;
    }
    leaveSoon();
  };
  const leaveSoon = (): void => {
    window.clearTimeout(hoverTimer);
    hoverTimer = window.setTimeout(() => {
      hover = null;
      render();
    }, HOVER_LEAVE_MS);
  };
  const onLeave = (): void => {
    if (!gesture && !drag) leaveSoon();
  };
  // Clicks on the controls must not take focus (and the cursor) out of the text.
  const keepFocus = (e: MouseEvent): void => {
    if ((e.target as Element).closest(".md-tbl-ctl")) e.preventDefault();
  };
  const onDocDown = (e: Event): void => {
    if (menu && !(e.target as Element).closest(".md-tbl-menu, .md-tbl-grip")) {
      closeMenu();
      render();
    }
  };
  const onKey = (e: KeyboardEvent): void => {
    if (e.key !== "Escape") return;
    if (drag) {
      e.preventDefault();
      e.stopPropagation();
      cancelDrag();
    } else if (menu) {
      e.preventDefault();
      e.stopPropagation();
      closeMenu();
      render();
      getView().focus();
    }
  };
  const onScroll = (): void => render();
  const resize = new ResizeObserver(() => render());
  resize.observe(surface);

  surface.addEventListener("mousemove", onMove);
  surface.addEventListener("mouseleave", onLeave);
  layer.addEventListener("mousedown", keepFocus);
  document.addEventListener("pointerdown", onDocDown, true);
  document.addEventListener("keydown", onKey, true);
  window.addEventListener("scroll", onScroll, true);

  /** Alt+arrows and Alt+Enter, from the editor. Returns true when handled. */
  function handleKey(e: KeyboardEvent): boolean {
    if (!e.altKey || e.ctrlKey || e.metaKey) return false;
    const at = selectionAt(getView());
    if (!at) return false;
    const { r, c } = indexes(at);
    const view = getView();
    switch (e.key) {
      case "ArrowUp":
        return moveRow(view.state, view.dispatch, r, r - 1) || true;
      case "ArrowDown":
        return moveRow(view.state, view.dispatch, r, r + 1) || true;
      case "ArrowLeft":
        return moveColumn(view.state, view.dispatch, c, c - 1) || true;
      case "ArrowRight":
        return moveColumn(view.state, view.dispatch, c, c + 1) || true;
      case "Enter": {
        hover = null;
        render();
        const grip = layer.querySelector<HTMLElement>(e.shiftKey ? ".md-tbl-grip-col" : ".md-tbl-grip-row");
        if (!grip) return false;
        if (e.shiftKey) openColumnMenu(at, grip);
        else openRowMenu(at, grip);
        return true;
      }
      default:
        return false;
    }
  }

  return {
    update: () => render(),
    handleKey,
    destroy: () => {
      window.clearTimeout(hoverTimer);
      endDrag();
      resize.disconnect();
      surface.removeEventListener("mousemove", onMove);
      surface.removeEventListener("mouseleave", onLeave);
      layer.removeEventListener("mousedown", keepFocus);
      document.removeEventListener("pointerdown", onDocDown, true);
      document.removeEventListener("keydown", onKey, true);
      window.removeEventListener("scroll", onScroll, true);
      layer.remove();
    },
  };
}
