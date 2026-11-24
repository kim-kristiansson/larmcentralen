// Table editing for the åtgärd editor: add and remove rows and columns, column alignment, and Tab in the last
// cell adding a row. Markdown tables always have exactly one header row (the first), so the header row can't be
// removed and nothing can be inserted above it.
import type { EditorState, Transaction } from "@milkdown/kit/prose/state";
import {
  CellSelection,
  addColumnAfter,
  addColumnBefore,
  deleteColumn,
  deleteRow,
  deleteTable,
  goToNextCell,
  isInTable,
  selectedRect,
} from "@milkdown/kit/prose/tables";

export type Align = "left" | "center" | "right";

export interface TableState {
  /** The selection touches the header row. */
  inHeader: boolean;
  /** Body rows (the header row not counted). */
  bodyRows: number;
  columns: number;
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
    align: alignment === "left" || alignment === "center" || alignment === "right" ? alignment : "",
  };
}

/** Whether a table button can be used right now; null for commands that aren't table commands. */
export function tableEnabled(cmd: string, t: TableState | null): boolean | null {
  switch (cmd) {
    case "rowBefore":
    case "rowDelete":
      return !!t && !t.inHeader;
    case "rowAfter":
    case "colBefore":
    case "colAfter":
    case "tableDelete":
    case "alignLeft":
    case "alignCenter":
    case "alignRight":
      return !!t;
    case "colDelete":
      return !!t && t.columns > 1;
    default:
      return null;
  }
}

/**
 * Removes the selected rows. Refused when the selection touches the header row: a Markdown table needs it.
 * Removing the last body row leaves a table with just its header, which is still valid Markdown.
 */
export function removeRows(state: EditorState, dispatch: Dispatch): boolean {
  const t = readTable(state);
  if (!t || t.inHeader) return false;
  return deleteRow(state, dispatch);
}

/** Removes the selected columns; refused for the only column (remove the table instead). */
export function removeColumns(state: EditorState, dispatch: Dispatch): boolean {
  const t = readTable(state);
  if (!t || t.columns <= 1) return false;
  return deleteColumn(state, dispatch);
}

export function removeTable(state: EditorState, dispatch: Dispatch): boolean {
  return deleteTable(state, dispatch);
}

export function columnBefore(state: EditorState, dispatch: Dispatch): boolean {
  return addColumnBefore(state, dispatch);
}

export function columnAfter(state: EditorState, dispatch: Dispatch): boolean {
  return addColumnAfter(state, dispatch);
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
