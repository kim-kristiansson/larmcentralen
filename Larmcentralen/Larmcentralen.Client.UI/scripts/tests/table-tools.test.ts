// Tests for table-tools.ts, run with `npm test` (bundled by esbuild, run by node:test; no browser needed).
// The schema mirrors Milkdown's GFM table: a table is one table_header_row followed by one or more table_rows, and
// every cell carries the column alignment. toMarkdown() writes a table the way Milkdown's GFM serializer does
// (alignment taken from the header row), so the tests can state the expected Markdown.
import { test } from "node:test";
import assert from "node:assert/strict";
import { Schema, type Node } from "@milkdown/kit/prose/model";
import { EditorState, TextSelection, type Transaction } from "@milkdown/kit/prose/state";
import { tableNodes } from "@milkdown/kit/prose/tables";
import { alignColumns, canMoveRow, moveColumn, moveRow, removeColumns, removeRows, readTable } from "../src/table-tools";

const base = tableNodes({
  tableGroup: "block",
  cellContent: "paragraph",
  cellAttributes: { alignment: { default: "left", getFromDOM: () => "left", setDOMAttr: () => {} } },
});
const schema = new Schema({
  nodes: {
    doc: { content: "block+" },
    paragraph: { group: "block", content: "text*" },
    text: {},
    table: { ...base.table, content: "table_header_row table_row+" },
    table_header_row: { ...base.table_row, content: "(table_header)*" },
    table_row: { ...base.table_row, content: "(table_cell)*" },
    table_header: base.table_header,
    table_cell: base.table_cell,
  },
});

type Align = "left" | "center" | "right";

function table(rows: string[][], align: Align[] = rows[0]!.map(() => "left")): Node {
  const n = schema.nodes;
  const cell = (type: "table_header" | "table_cell", text: string, i: number) =>
    n[type]!.create({ alignment: align[i] }, n["paragraph"]!.create(null, text ? schema.text(text) : null));
  const [head, ...body] = rows;
  return n["doc"]!.create(null, [
    n["table"]!.create(null, [
      n["table_header_row"]!.create(null, head!.map((t, i) => cell("table_header", t, i))),
      ...body.map((r) => n["table_row"]!.create(null, r.map((t, i) => cell("table_cell", t, i)))),
    ]),
  ]);
}

/** State with the cursor in cell (row, col); row 0 is the header. */
function stateAt(doc: Node, row: number, col: number): EditorState {
  let pos = -1;
  let r = -1;
  doc.descendants((node, p) => {
    if (node.type.name.endsWith("row")) {
      r++;
      let c = -1;
      node.forEach((_cell, offset) => {
        c++;
        if (r === row && c === col) pos = p + 1 + offset + 2;
      });
      return false;
    }
    return true;
  });
  const state = EditorState.create({ doc });
  return state.apply(state.tr.setSelection(TextSelection.create(doc, pos)));
}

function run(state: EditorState, command: (s: EditorState, d: (tr: Transaction) => void) => boolean): { ok: boolean; state: EditorState } {
  let next = state;
  const ok = command(state, (tr) => (next = state.apply(tr)));
  return { ok, state: next };
}

/** GFM table as Milkdown writes it: header row, alignment row from the header cells, body rows. */
function toMarkdown(doc: Node): string {
  const lines: string[] = [];
  doc.firstChild!.forEach((row, _o, i) => {
    const cells: string[] = [];
    const aligns: string[] = [];
    row.forEach((cell) => {
      cells.push(cell.textContent);
      const a = cell.attrs["alignment"] as Align;
      aligns.push(a === "center" ? ":-:" : a === "right" ? "-:" : ":-");
    });
    lines.push(`| ${cells.join(" | ")} |`);
    if (i === 0) lines.push(`| ${aligns.join(" | ")} |`);
  });
  return lines.join("\n");
}

const sample = () =>
  table(
    [
      ["Givare", "Värde", "Enhet"],
      ["TE-101", "4–20", "mA"],
      ["TE-102", "0–10", "V"],
      ["PT-201", "4–20", "mA"],
    ],
    ["left", "center", "right"],
  );

test("moving a body row down gives the rows in the new order", () => {
  const { ok, state } = run(stateAt(sample(), 1, 0), (s, d) => moveRow(s, d, 1, 3));
  assert.equal(ok, true);
  assert.equal(
    toMarkdown(state.doc),
    ["| Givare | Värde | Enhet |", "| :- | :-: | -: |", "| TE-102 | 0–10 | V |", "| PT-201 | 4–20 | mA |", "| TE-101 | 4–20 | mA |"].join("\n"),
  );
  assert.equal(readTable(state)?.row, 3, "the cursor follows the row");
});

test("moving a body row up to just under the header works", () => {
  const { ok, state } = run(stateAt(sample(), 3, 1), (s, d) => moveRow(s, d, 3, 1));
  assert.equal(ok, true);
  assert.match(toMarkdown(state.doc), /^\| Givare .*\n.*\n\| PT-201 \| 4–20 \| mA \|\n\| TE-101/);
});

test("the header row can't be moved, and nothing can be moved above it", () => {
  assert.equal(run(stateAt(sample(), 0, 0), (s, d) => moveRow(s, d, 0, 2)).ok, false);
  assert.equal(run(stateAt(sample(), 2, 0), (s, d) => moveRow(s, d, 2, 0)).ok, false);
  assert.equal(canMoveRow(4, 1, 0), false);
  assert.equal(canMoveRow(4, 0, 1), false);
  assert.equal(canMoveRow(4, 2, 1), true);
});

test("moving a column takes its header text and alignment along", () => {
  const { ok, state } = run(stateAt(sample(), 2, 2), (s, d) => moveColumn(s, d, 2, 0));
  assert.equal(ok, true);
  assert.equal(
    toMarkdown(state.doc),
    ["| Enhet | Givare | Värde |", "| -: | :- | :-: |", "| mA | TE-101 | 4–20 |", "| V | TE-102 | 0–10 |", "| mA | PT-201 | 4–20 |"].join("\n"),
  );
  assert.equal(readTable(state)?.column, 0, "the cursor follows the column");
  // The header row is still the header row.
  assert.equal(state.doc.firstChild!.firstChild!.type.name, "table_header_row");
});

test("the header row and the last row under it can't be removed", () => {
  assert.equal(run(stateAt(sample(), 0, 1), removeRows).ok, false);
  const twoRows = table([["A", "B"], ["1", "2"]]);
  assert.equal(run(stateAt(twoRows, 1, 0), removeRows).ok, false);
  const removed = run(stateAt(sample(), 2, 0), removeRows);
  assert.equal(removed.ok, true);
  assert.doesNotMatch(toMarkdown(removed.state.doc), /TE-102/);
});

test("the only column can't be removed", () => {
  const one = table([["A"], ["1"]]);
  assert.equal(run(stateAt(one, 1, 0), removeColumns).ok, false);
  assert.equal(run(stateAt(sample(), 1, 1), removeColumns).ok, true);
});

test("alignment is set on the column's header cell", () => {
  const { state } = run(stateAt(sample(), 2, 0), (s, d) => alignColumns(s, d, "right"));
  assert.match(toMarkdown(state.doc), /^\| Givare \| Värde \| Enhet \|\n\| -: \| :-: \| -: \|/);
});
