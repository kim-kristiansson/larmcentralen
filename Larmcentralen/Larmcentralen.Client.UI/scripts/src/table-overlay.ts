// Buttons on the table itself, instead of a toolbar row: a grip on the left of every row and on top of every
// column (opens a small menu), + buttons between rows and columns, "add row" and "add column" bars along the
// bottom and right edge, and a bin in the top-left corner. They show while the cursor is in a table (so they work
// on touch screens) and while the mouse is over one. Everything is drawn in an overlay next to ProseMirror's DOM,
// never inside it, so the editor's own DOM is untouched.
import type { EditorView } from "@milkdown/kit/prose/view";
import { TextSelection, type EditorState, type Transaction } from "@milkdown/kit/prose/state";
import { addColumnAfter, addColumnBefore, findTable } from "@milkdown/kit/prose/tables";
import { alignColumns, removeColumns, removeRows, removeTable, type Align } from "./table-tools";

const sv = {
  rowGrip: "Rad: infoga eller ta bort",
  colGrip: "Kolumn: infoga, justera eller ta bort",
  insertRowHere: "Infoga rad här",
  insertColHere: "Infoga kolumn här",
  addRow: "Lägg till rad",
  addCol: "Lägg till kolumn",
  deleteTable: "Ta bort tabell",
  rowAbove: "Infoga rad ovanför",
  rowBelow: "Infoga rad nedanför",
  deleteRow: "Ta bort rad",
  colLeft: "Infoga kolumn till vänster",
  colRight: "Infoga kolumn till höger",
  alignLeft: "Vänsterjustera",
  alignCenter: "Centrera",
  alignRight: "Högerjustera",
  deleteCol: "Ta bort kolumn",
  headerRow: "Rubrikraden måste finnas kvar",
  lastRow: "En tabell behöver minst en rad under rubrikraden",
  lastCol: "Den enda kolumnen kan inte tas bort. Ta bort tabellen i stället.",
  menuRow: "Rad",
  menuCol: "Kolumn",
};

const icons = {
  gripV: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="6" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="12" cy="18" r="1.8"/></svg>',
  gripH: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="6" cy="12" r="1.8"/><circle cx="12" cy="12" r="1.8"/><circle cx="18" cy="12" r="1.8"/></svg>',
  plus: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 5v14M5 12h14"/></svg>',
  trash: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6"/></svg>',
  above: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="13" width="18" height="7" rx="1.5"/><path d="M12 3v7M8.5 6.5h7"/></svg>',
  below: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3" y="4" width="18" height="7" rx="1.5"/><path d="M12 14v7M8.5 17.5h7"/></svg>',
  left: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="13" y="3" width="7" height="18" rx="1.5"/><path d="M3 12h7M6.5 8.5v7"/></svg>',
  right: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="4" y="3" width="7" height="18" rx="1.5"/><path d="M14 12h7M17.5 8.5v7"/></svg>',
  alignLeft: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M4 10h10M4 14h16M4 18h10"/></svg>',
  alignCenter: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M7 10h10M4 14h16M7 18h10"/></svg>',
  alignRight: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 6h16M10 10h10M4 14h16M10 18h10"/></svg>',
};

type Command = (state: EditorState, dispatch: (tr: Transaction) => void) => boolean;

interface MenuItem {
  icon: string;
  label: string;
  run: () => void;
  disabled?: string; // tooltip saying why
  danger?: boolean;
  pressed?: boolean;
}

export interface TableOverlay {
  update: (view: EditorView) => void;
  destroy: () => void;
}

/**
 * Overlay for the editor's tables. Rows are added with Milkdown's own commands (`rowBefore`, `rowAfter`): they create
 * body rows (never a second header row) and keep the column alignment.
 */
export function createTableOverlay(surface: HTMLElement, getView: () => EditorView, rowBefore: () => void, rowAfter: () => void): TableOverlay {
  const layer = document.createElement("div");
  layer.className = "md-tbl-layer";
  surface.appendChild(layer);

  let hovered: HTMLTableElement | null = null;
  let menu: HTMLElement | null = null;
  let menuFor: HTMLTableElement | null = null;

  const tableOfSelection = (view: EditorView): HTMLTableElement | null => {
    const found = findTable(view.state.selection.$from);
    if (!found) return null;
    const dom = view.nodeDOM(found.pos);
    if (!(dom instanceof HTMLElement)) return null;
    return dom instanceof HTMLTableElement ? dom : dom.querySelector("table");
  };

  /** Puts the cursor in a cell, then runs a table command there. */
  const inCell = (cell: HTMLTableCellElement, command: Command | (() => void)): void => {
    const view = getView();
    const pos = view.posAtDOM(cell, 0);
    view.dispatch(view.state.tr.setSelection(TextSelection.near(view.state.doc.resolve(pos))));
    if (command.length === 0) (command as () => void)();
    else (command as Command)(getView().state, getView().dispatch);
    closeMenu();
    getView().focus();
  };

  const button = (cls: string, title: string, icon: string, x: number, y: number, onClick: () => void): HTMLButtonElement => {
    const b = document.createElement("button");
    b.type = "button";
    b.className = `md-tbl-btn ${cls}`;
    b.title = title;
    b.setAttribute("aria-label", title);
    b.innerHTML = icon;
    b.style.left = `${x}px`;
    b.style.top = `${y}px`;
    b.addEventListener("click", (e) => {
      e.preventDefault();
      onClick();
    });
    layer.appendChild(b);
    return b;
  };

  function closeMenu(): void {
    menu?.remove();
    menu = null;
    menuFor = null;
  }

  function openMenu(anchor: HTMLElement, table: HTMLTableElement, title: string, items: MenuItem[]): void {
    closeMenu();
    menuFor = table;
    const m = document.createElement("div");
    m.className = "md-tbl-menu";
    m.setAttribute("role", "menu");
    m.setAttribute("aria-label", title);
    for (const item of items) {
      const b = document.createElement("button");
      b.type = "button";
      b.setAttribute("role", "menuitem");
      b.className = "md-tbl-item" + (item.danger ? " md-tbl-danger" : "");
      b.innerHTML = `${item.icon}<span>${item.label}</span>`;
      if (item.pressed) b.setAttribute("aria-pressed", "true");
      if (item.disabled) {
        b.disabled = true;
        b.title = item.disabled;
      }
      b.addEventListener("click", (e) => {
        e.preventDefault();
        item.run();
      });
      m.appendChild(b);
    }
    layer.appendChild(m);
    // Below the grip, kept inside the editor horizontally.
    const surfaceRect = surface.getBoundingClientRect();
    const a = anchor.getBoundingClientRect();
    let left = a.left - surfaceRect.left;
    const maxLeft = surface.clientWidth - m.offsetWidth - 8;
    if (left > maxLeft) left = Math.max(8, maxLeft);
    m.style.left = `${left}px`;
    m.style.top = `${a.bottom - surfaceRect.top + 4}px`;
    menu = m;
    (m.querySelector("button:not(:disabled)") as HTMLButtonElement | null)?.focus();
  }

  function render(): void {
    const view = getView();
    const table = menuFor ?? tableOfSelection(view) ?? hovered;
    layer.querySelectorAll(".md-tbl-btn").forEach((b) => b.remove());
    if (!table || !table.isConnected) {
      closeMenu();
      return;
    }
    const base = surface.getBoundingClientRect();
    const t = table.getBoundingClientRect();
    const left = t.left - base.left;
    const top = t.top - base.top;
    const rows = Array.from(table.rows);
    const header = rows[0];
    if (!header) return;
    const columns = header.cells.length;
    const bodyRows = rows.length - 1;
    const gutter = 20; // centre of the grips, this far outside the table

    // Row grips (centre of each row) and + at the boundaries between rows.
    rows.forEach((row, r) => {
      const rr = row.getBoundingClientRect();
      const firstCell = row.cells[0];
      if (!firstCell) return;
      button("md-tbl-grip", `${sv.rowGrip}`, icons.gripV, left - gutter, rr.top - base.top + rr.height / 2, () => {
        const grip = layer.querySelector<HTMLElement>(`.md-tbl-grip[data-row="${r}"]`);
        openMenu(grip ?? layer, table, sv.menuRow, [
          { icon: icons.above, label: sv.rowAbove, run: () => inCell(firstCell, rowBefore), ...(r === 0 ? { disabled: sv.headerRow } : {}) },
          { icon: icons.below, label: sv.rowBelow, run: () => inCell(firstCell, rowAfter) },
          {
            icon: icons.trash,
            label: sv.deleteRow,
            danger: true,
            run: () => inCell(firstCell, removeRows),
            ...(r === 0 ? { disabled: sv.headerRow } : bodyRows <= 1 ? { disabled: sv.lastRow } : {}),
          },
        ]);
      }).dataset.row = String(r);
      if (r < rows.length - 1) {
        button("md-tbl-plus", sv.insertRowHere, icons.plus, left - gutter, rr.bottom - base.top, () => inCell(firstCell, rowAfter));
      }
    });

    // Column grips (centre of each column) and + at the boundaries between columns.
    Array.from(header.cells).forEach((cell, c) => {
      const cr = cell.getBoundingClientRect();
      const align = (["left", "center", "right"] as Align[]).find((a) => cell.style.textAlign === a || cell.getAttribute("align") === a);
      button("md-tbl-grip md-tbl-grip-col", sv.colGrip, icons.gripH, cr.left - base.left + cr.width / 2, top - gutter, () => {
        const grip = layer.querySelector<HTMLElement>(`.md-tbl-grip-col[data-col="${c}"]`);
        openMenu(grip ?? layer, table, sv.menuCol, [
          { icon: icons.left, label: sv.colLeft, run: () => inCell(cell, addColumnBefore) },
          { icon: icons.right, label: sv.colRight, run: () => inCell(cell, addColumnAfter) },
          { icon: icons.alignLeft, label: sv.alignLeft, pressed: align === "left", run: () => inCell(cell, (s, d) => alignColumns(s, d, "left")) },
          { icon: icons.alignCenter, label: sv.alignCenter, pressed: align === "center", run: () => inCell(cell, (s, d) => alignColumns(s, d, "center")) },
          { icon: icons.alignRight, label: sv.alignRight, pressed: align === "right", run: () => inCell(cell, (s, d) => alignColumns(s, d, "right")) },
          { icon: icons.trash, label: sv.deleteCol, danger: true, run: () => inCell(cell, removeColumns), ...(columns <= 1 ? { disabled: sv.lastCol } : {}) },
        ]);
      }).dataset.col = String(c);
      if (c < columns - 1) {
        button("md-tbl-plus", sv.insertColHere, icons.plus, cr.right - base.left, top - gutter, () => inCell(cell, addColumnAfter));
      }
    });

    // Add a row at the end / a column at the end: thin bars along the bottom and right edge.
    const lastRow = rows[rows.length - 1];
    const lastFirst = lastRow?.cells[0];
    if (lastFirst) {
      const bar = button("md-tbl-bar md-tbl-bar-row", sv.addRow, icons.plus, left + t.width / 2, top + t.height + 18, () => inCell(lastFirst, rowAfter));
      bar.style.width = `${t.width}px`;
    }
    const lastHeader = header.cells[columns - 1];
    if (lastHeader) {
      const bar = button("md-tbl-bar md-tbl-bar-col", sv.addCol, icons.plus, left + t.width + 18, top + t.height / 2, () => inCell(lastHeader, addColumnAfter));
      bar.style.height = `${t.height}px`;
    }

    // Remove the whole table: corner bin.
    const first = header.cells[0];
    if (first) button("md-tbl-trash", sv.deleteTable, icons.trash, left - gutter, top - gutter, () => inCell(first, removeTable));
  }

  // --- Hover, outside clicks, Escape, layout changes --------------------------------------------
  const onMove = (e: MouseEvent): void => {
    const target = e.target as Element;
    if (target.closest(".md-tbl-layer")) return;
    const table = target.closest<HTMLTableElement>(".ProseMirror table");
    if (table !== hovered) {
      hovered = table;
      render();
    }
  };
  const onLeave = (): void => {
    hovered = null;
    render();
  };
  // Clicks on overlay buttons must not take focus (and the cursor) out of the text.
  const keepFocus = (e: MouseEvent): void => {
    if ((e.target as Element).closest(".md-tbl-btn")) e.preventDefault();
  };
  const onDocDown = (e: MouseEvent | TouchEvent): void => {
    if (menu && !(e.target as Element).closest(".md-tbl-menu, .md-tbl-grip")) {
      closeMenu();
      render();
    }
  };
  const onKey = (e: KeyboardEvent): void => {
    if (e.key === "Escape" && menu) {
      e.preventDefault();
      closeMenu();
      render();
      getView().focus();
    }
  };
  const resize = new ResizeObserver(() => render());
  resize.observe(surface);

  surface.addEventListener("mousemove", onMove);
  surface.addEventListener("mouseleave", onLeave);
  layer.addEventListener("mousedown", keepFocus);
  document.addEventListener("mousedown", onDocDown, true);
  document.addEventListener("touchstart", onDocDown, true);
  document.addEventListener("keydown", onKey, true);

  return {
    update: () => render(),
    destroy: () => {
      resize.disconnect();
      surface.removeEventListener("mousemove", onMove);
      surface.removeEventListener("mouseleave", onLeave);
      layer.removeEventListener("mousedown", keepFocus);
      document.removeEventListener("mousedown", onDocDown, true);
      document.removeEventListener("touchstart", onDocDown, true);
      document.removeEventListener("keydown", onKey, true);
      layer.remove();
    },
  };
}
