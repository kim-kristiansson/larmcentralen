// The Milkdown editor as åtgärder use it: presets, plugins and Markdown output settings. Shared by the app
// (milkdown-interop.ts adds the toolbar, link bar and table controls on top) and the round-trip tests, so the
// tests check exactly the Markdown the app writes.
import { Editor, rootCtx, defaultValueCtx, editorViewCtx, remarkStringifyOptionsCtx } from "@milkdown/kit/core";
import type { Ctx } from "@milkdown/kit/ctx";
import { commonmark, remarkPreserveEmptyLinePlugin, imageSchema } from "@milkdown/kit/preset/commonmark";
import { gfm, remarkGFMPlugin } from "@milkdown/kit/preset/gfm";
import { history } from "@milkdown/kit/plugin/history";
import { clipboard } from "@milkdown/kit/plugin/clipboard";
import { trailing } from "@milkdown/kit/plugin/trailing";
import { $view, getMarkdown as readMarkdown, type $Prose } from "@milkdown/kit/utils";
import type { Node as ProseNode } from "@milkdown/kit/prose/model";
import type { NodeView } from "@milkdown/kit/prose/view";
import { isAllowedUrl, isVideoUrl } from "./url";

/**
 * Markdown style, so text is written the same way every time: "-" bullets, "---" rules, "*" for emphasis and
 * "**" for bold (also for text that came in with "_"), fenced code, and tables without padding.
 */
function configureOutput(ctx: Ctx): void {
  ctx.update(remarkStringifyOptionsCtx, (prev) => {
    const handlers = prev.handlers ?? {};
    // Milkdown keeps the marker a text was typed with (node.marker); drop it so "*" is always used.
    type Handle = (node: Record<string, unknown>, ...rest: unknown[]) => string;
    const withoutMarker = (name: "emphasis" | "strong"): Record<string, Handle> => {
      const original = (handlers as Record<string, Handle | undefined>)[name];
      if (!original) return {};
      return { [name]: (node, ...rest) => original({ ...node, marker: undefined }, ...rest) };
    };
    return {
      ...prev,
      bullet: "-" as const,
      rule: "-" as const,
      emphasis: "*" as const,
      strong: "*" as const,
      fence: "`" as const,
      fences: true,
      handlers: { ...handlers, ...withoutMarker("emphasis"), ...withoutMarker("strong") } as typeof handlers,
    };
  });
  ctx.set(remarkGFMPlugin.options.key, { tablePipeAlign: false });
}

/** Shows an image whose address ends in .mp4 or .webm as a video, as the reader does; other images as images. */
const mediaView = $view(imageSchema.node, () => (initial: ProseNode): NodeView => {
  let node = initial;
  const build = (n: ProseNode): HTMLElement => {
    const src = String(n.attrs["src"] ?? "");
    const alt = String(n.attrs["alt"] ?? "");
    const safe = isAllowedUrl(src);
    if (safe && isVideoUrl(src)) {
      const video = document.createElement("video");
      video.controls = true;
      video.preload = "metadata";
      video.src = src;
      video.title = alt;
      video.className = "md-video";
      video.setAttribute("contenteditable", "false");
      return video;
    }
    const img = document.createElement("img");
    if (safe) img.src = src;
    img.alt = alt;
    if (n.attrs["title"]) img.title = String(n.attrs["title"]);
    return img;
  };
  const view: NodeView = {
    dom: build(node),
    update: (next) => {
      if (next.type !== node.type) return false;
      const kind = (n: ProseNode) => isVideoUrl(String(n.attrs["src"] ?? ""));
      if (kind(next) !== kind(node)) return false; // video <-> image: let ProseMirror rebuild
      node = next;
      const fresh = build(next);
      (view.dom as HTMLElement).replaceWith(fresh);
      (view as { dom: Node }).dom = fresh;
      return true;
    },
    // Let the video's own controls work instead of selecting the node.
    stopEvent: (e) => view.dom instanceof HTMLVideoElement && e.type !== "dragstart" && e.target === view.dom,
    ignoreMutation: () => true,
  };
  return view;
});

/** Creates the editor with everything an åtgärd needs. `extra` adds the app's own config and plugins. */
export async function createEditor(
  root: HTMLElement,
  markdown: string,
  extra?: { config?: (ctx: Ctx) => void; plugins?: $Prose[] },
): Promise<Editor> {
  const editor = Editor.make()
    .config((ctx) => {
      ctx.set(rootCtx, root);
      ctx.set(defaultValueCtx, markdown);
      configureOutput(ctx);
      extra?.config?.(ctx);
    })
    .use(commonmark)
    .use(gfm)
    .use(history)
    .use(clipboard)
    // Always an empty paragraph after a table or code block at the end, so there is somewhere to keep writing.
    .use(trailing)
    .use(mediaView);
  for (const p of extra?.plugins ?? []) editor.use(p);
  // Milkdown writes an empty paragraph as a raw "<br />" line so blank lines survive a round trip. Åtgärder never
  // render raw HTML, so that plugin is taken out, the documented way.
  await editor.remove(remarkPreserveEmptyLinePlugin);
  return editor.create();
}

/**
 * Tidies the document before it is saved: removes links and images whose address isn't allowed (javascript:,
 * data:, …; links keep their text), and drops an image title that only repeats its text (pasting Markdown makes
 * one: ![Film](film.mp4 "Film")). Returns true when something changed.
 */
export function removeUnsafeLinks(editor: Editor): boolean {
  const view = editor.ctx.get(editorViewCtx);
  const { state } = view;
  const link = state.schema.marks["link"];
  let tr = state.tr;
  const images: { from: number; to: number }[] = [];
  state.doc.descendants((node, pos) => {
    if (node.type.name === "image") {
      if (!isAllowedUrl(String(node.attrs["src"] ?? ""))) images.push({ from: pos, to: pos + node.nodeSize });
      else if (node.attrs["title"] && node.attrs["title"] === node.attrs["alt"]) tr = tr.setNodeMarkup(pos, undefined, { ...node.attrs, title: "" });
    }
    if (link && node.isText) {
      const mark = link.isInSet(node.marks);
      if (mark && !isAllowedUrl(String(mark.attrs["href"] ?? ""))) tr = tr.removeMark(pos, pos + node.nodeSize, link);
    }
  });
  for (const img of images.reverse()) tr = tr.delete(tr.mapping.map(img.from), tr.mapping.map(img.to));
  if (!tr.docChanged) return false;
  view.dispatch(tr);
  return true;
}

/** The document as Markdown, without trailing blank lines; unsafe links and images are removed first. */
export function markdownOf(editor: Editor): string {
  removeUnsafeLinks(editor);
  const markdown = editor.action(readMarkdown());
  // Safety net in case an empty paragraph still comes out as a raw "<br />" line: never store raw HTML for it.
  return markdown
    .split("\n")
    .filter((line) => !/^\s*<br\s*\/?>\s*$/i.test(line))
    .join("\n")
    .replace(/\n{3,}/g, "\n\n")
    .trim();
}
