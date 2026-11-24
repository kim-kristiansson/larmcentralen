// The Varning and Info boxes: GitHub alert syntax, so åtgärder stay plain Markdown.
//
//   > [!WARNING]          > [!NOTE]
//   > Bryt strömmen!      > Ugnen behöver svalna.
//
// In the editor both are a blockquote with an `alert` attribute ("warning" or "note"); a blockquote without one is a
// plain, neutral quote. Reading: a remark plugin takes the "[!KIND]" line off and sets `alert`. Writing: the
// blockquote handler puts the line back. CAUTION reads as warning and TIP / IMPORTANT as note; the editor only
// writes WARNING and NOTE. (A future Word export maps WARNING to the style "Varning" and NOTE to "Info".)
import { blockquoteSchema } from "@milkdown/kit/preset/commonmark";
import { $remark } from "@milkdown/kit/utils";
import type { Ctx } from "@milkdown/kit/ctx";
import { defaultHandlers } from "mdast-util-to-markdown";

export type AlertKind = "warning" | "note";

const kinds: Record<string, AlertKind> = { WARNING: "warning", CAUTION: "warning", NOTE: "note", TIP: "note", IMPORTANT: "note" };
const marker: Record<AlertKind, string> = { warning: "WARNING", note: "NOTE" };
const markerLine = /^\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\][ \t]*(?:\r?\n|$)/i;

interface MdNode {
  type: string;
  value?: string;
  alert?: AlertKind;
  children?: MdNode[];
}

/** Remark plugin (used when Markdown is read): turns "> [!KIND]" blockquotes into blockquotes with `alert`. */
export const remarkAlerts = $remark("larmAlerts", () => () => (tree: unknown) => {
  const visit = (node: MdNode): void => {
    if (node.type === "blockquote") markAlert(node);
    node.children?.forEach(visit);
  };
  visit(tree as MdNode);
});

function markAlert(quote: MdNode): void {
  const paragraph = quote.children?.[0];
  const first = paragraph?.type === "paragraph" ? paragraph.children?.[0] : undefined;
  if (!paragraph || !first || first.type !== "text" || first.value === undefined) return;
  const match = markerLine.exec(first.value);
  if (!match?.[1]) return;
  const kind = kinds[match[1].toUpperCase()];
  if (!kind) return;
  quote.alert = kind;
  first.value = first.value.slice(match[0].length);
  const children = paragraph.children!;
  if (first.value === "") {
    children.shift();
    if (children[0]?.type === "break") children.shift();
  }
  if (children.length === 0) quote.children!.shift();
  if (quote.children!.length === 0) quote.children!.push({ type: "paragraph", children: [] });
}

/** Adds the `alert` attribute to Milkdown's blockquote and reads/writes it, via the schema's own config slice. */
export function configureAlerts(ctx: Ctx): void {
  ctx.update(blockquoteSchema.key, (prev) => (c) => {
    const base = prev(c);
    return {
      ...base,
      attrs: { alert: { default: null } },
      parseDOM: [
        {
          tag: "blockquote",
          getAttrs: (dom) => {
            const kind = dom instanceof HTMLElement ? dom.dataset["alert"] : undefined;
            return { alert: kind === "warning" || kind === "note" ? kind : null };
          },
        },
      ],
      toDOM: (node) => {
        const alert = node.attrs["alert"] as AlertKind | null;
        return ["blockquote", alert ? { "data-alert": alert, class: `callout callout-${alert}` } : {}, 0];
      },
      parseMarkdown: {
        match: ({ type }) => type === "blockquote",
        runner: (state, node, type) => {
          state.openNode(type, { alert: (node as MdNode).alert ?? null }).next(node.children).closeNode();
        },
      },
      toMarkdown: {
        match: (node) => node.type.name === "blockquote",
        runner: (state, node) => {
          const alert = node.attrs["alert"] as AlertKind | null;
          state.openNode("blockquote", undefined, alert ? { alert } : undefined).next(node.content).closeNode();
        },
      },
    };
  });
}

/** Blockquote writer for remark-stringify: the normal blockquote, with the "[!KIND]" line first for an alert. */
export function blockquoteHandler(node: MdNode, parent: unknown, state: unknown, info: unknown): string {
  const plain = (defaultHandlers.blockquote as unknown as (n: unknown, p: unknown, s: unknown, i: unknown) => string)(node, parent, state, info);
  const kind = node.alert;
  if (!kind) return plain;
  return `> [!${marker[kind]}]\n${plain}`;
}
