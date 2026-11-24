// Table editing for the åtgärd editor: add, remove and move rows and columns, column alignment, and Tab in the last
// cell adding a row. A Markdown table has exactly one header row and it is the first row, so the header row can't be
// removed or moved and nothing can be moved above it. Milkdown's table also needs at least one row under the header.
// Column alignment lives on every cell of a column, so moving a column takes its alignment along.
import { TextSelection, type EditorState, type Transaction } from "@milkdown/kit/prose/state";
import {
  CellSelection,
  TableMap,
  deleteColumn,
  deleteRow,
  deleteTable,
  findTable,
  goToNextCell,
  isInTable,
  moveTableColumn,
  moveTableRow,
  selectedRect,
} from "@milkdown/kit/prose/tables";

export type Align = "left" | "center" | "right";

export interface TableState {
  /** The selection touches the header row. */
  inHeader: boolean;
  /** Body rows (the header row not counted). */
  bodyRows: number;
  columns: number;
  /** Row and column of the cell the selection starts in. */
  row: number;
  column: number;
  /** Alignment of the column the selection starts in, from its header cell. */
  align: Align | "";
}

type Dispatch = (tr: Transaction) => void;

export function readTable(state: EditorState): TableState | null {
  if (!isInTable(state)) return null;
  const rect = selectedRect(state);
  const headerCellPos = rect.tableStart + (rect.map.map[rect.left] ?? 0);
  const alignment = state.doc.nodeAt(headerCellPos)?.attrs["alignment"];
  return {
    inHeader: rect.top === 0,
    bodyRows: rect.map.height - 1,
    columns: rect.map.width,
    row: rect.top,
    column: rect.left,
    align: alignment === "left" || alignment === "center" || alignment === "right" ? alignment : "",
  };
}

// --- What is allowed (the menus grey out the rest) ------------------------------------------------

/** The header row (0) stays put; body rows move between 1 and the last row. */
export const canMoveRow = (rows: number, from: number, to: number): boolean =>
  from >= 1 && to >= 1 && from < rows && to < rows && from !== to;

export const canMoveColumn = (columns: number, from: number, to: number): boolean =>
  from >= 0 && to >= 0 && from < columns && to < columns && from !== to;

/** Not the header row, and not the only row under it. */
export const canRemoveRow = (rows: number, row: number): boolean => row >= 1 && rows - 1 > 1;

export const canRemoveColumn = (columns: number): boolean => columns > 1;

// --- Commands ---------------------------------------------------------------------------------------

/** Removes the row with the cursor, if allowed (see canRemoveRow). */
export function removeRows(state: EditorState, dispatch: Dispatch): boolean {
  const t = readTable(state);
  if (!t || t.inHeader || !canRemoveRow(t.bodyRows + 1, t.row)) return false;
  return deleteRow(state, dispatch);
}

/** Removes the column with the cursor; refused for the only column (remove the table instead). */
export function removeColumns(state: EditorState, dispatch: Dispatch): boolean {
  const t = readTable(state);
  if (!t || !canRemoveColumn(t.columns)) return false;
  return deleteColumn(state, dispatch);
}

export function removeTable(state: EditorState, dispatch: Dispatch): boolean {
  return deleteTable(state, dispatch);
}

/**
 * Moves row `from` so it ends up at index `to` (both counted with the header row as 0), in the table with the
 * cursor. The cursor follows the row. Refused for the header row and for any move above it.
 */
export function moveRow(state: EditorState, dispatch: Dispatch, from: number, to: number): boolean {
  const table = findTable(state.selection.$from);
  if (!table) return false;
  const map = TableMap.get(table.node);
  if (!canMoveRow(map.height, from, to)) return false;
  const column = Math.max(0, readTable(state)?.column ?? 0);
  const out: { tr?: Transaction } = {};
  const ok = moveTableRow({ from, to, select: false, pos: table.start })(state, (tr) => (out.tr = tr));
  if (!ok || !out.tr) return false;
  dispatch(cursorAt(out.tr, table.pos, to, column));
  return true;
}

/** Moves column `from` so it ends up at index `to`, header cell and alignment included. The cursor follows it. */
export function moveColumn(state: EditorState, dispatch: Dispatch, from: number, to: number): boolean {
  const table = findTable(state.selection.$from);
  if (!table) return false;
  const map = TableMap.get(table.node);
  if (!canMoveColumn(map.width, from, to)) return false;
  const row = Math.max(0, readTable(state)?.row ?? 0);
  const out: { tr?: Transaction } = {};
  const ok = moveTableColumn({ from, to, select: false, pos: table.start })(state, (tr) => (out.tr = tr));
  if (!ok || !out.tr) return false;
  dispatch(cursorAt(out.tr, table.pos, row, to));
  return true;
}

/** Puts the cursor at the start of a cell of the table that starts at `tablePos`. */
function cursorAt(tr: Transaction, tablePos: number, row: number, column: number): Transaction {
  const table = tr.doc.nodeAt(tablePos);
  if (!table) return tr;
  const map = TableMap.get(table);
  const cell = map.map[Math.min(row, map.height - 1) * map.width + Math.min(column, map.width - 1)];
  if (cell === undefined) return tr;
  return tr.setSelection(TextSelection.near(tr.doc.resolve(tablePos + 1 + cell + 1)));
}

/**
 * Aligns the selected columns. Markdown keeps alignment per column in the header row; Milkdown copies the header
 * cell's alignment to the cells below, so only the header cells are set here.
 */
export function alignColumns(state: EditorState, dispatch: Dispatch, align: Align): boolean {
  if (!isInTable(state)) return false;
  const rect = selectedRect(state);
  let tr = state.tr;
  for (let col = rect.left; col < rect.right; col++) {
    const pos = rect.tableStart + (rect.map.map[col] ?? 0);
    const cell = tr.doc.nodeAt(pos);
    if (cell) tr = tr.setNodeMarkup(pos, undefined, { ...cell.attrs, alignment: align });
  }
  dispatch(tr);
  return true;
}

/** True when the cursor is in the table's last cell, so Tab has nowhere to go. */
export function inLastCell(state: EditorState): boolean {
  if (!isInTable(state) || state.selection instanceof CellSelection) return false;
  return !goToNextCell(1)(state);
}

/** Tab from the last cell: a new row, cursor in its first cell. */
export function nextCellOrNewRow(state: EditorState, dispatch: Dispatch, addRowAfter: () => void, getState: () => EditorState): boolean {
  if (!inLastCell(state)) return goToNextCell(1)(state, dispatch);
  addRowAfter();
  return goToNextCell(1)(getState(), dispatch);
}

/**
 * How far to scroll (px, negative = up) during one frame of a drag. Only row drags scroll, only when the pointer is
 * within `edge` px of the visible top or bottom, and only if the table continues past that edge; never faster than
 * `maxSpeed`. Everything is in viewport coordinates.
 */
export function autoScrollStep(
  axis: "row" | "col",
  pointerY: number,
  visibleTop: number,
  visibleBottom: number,
  tableTop: number,
  tableBottom: number,
  edge: number,
  maxSpeed: number,
): number {
  if (axis !== "row") return 0;
  if (pointerY < visibleTop + edge && tableTop < visibleTop) return -Math.min(maxSpeed, Math.ceil((visibleTop + edge - pointerY) / 6));
  if (pointerY > visibleBottom - edge && tableBottom > visibleBottom) return Math.min(maxSpeed, Math.ceil((pointerY - (visibleBottom - edge)) / 6));
  return 0;
}