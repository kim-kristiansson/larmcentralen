// Thin wrapper around the locally bundled Quill 2. Loaded on demand by QuillEditor.razor.
const base = "./_content/Larmcentralen.Client.UI/lib/quill/";
let loading;

function loadQuill() {
  if (window.Quill) return Promise.resolve();
  if (loading) return loading;
  loading = new Promise((resolve, reject) => {
    const css = document.createElement("link");
    css.rel = "stylesheet";
    css.href = base + "quill.snow.css";
    document.head.appendChild(css);

    const script = document.createElement("script");
    script.src = base + "quill.js";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("Could not load Quill"));
    document.head.appendChild(script);
  });
  return loading;
}

export async function create(host, html, placeholder) {
  await loadQuill();
  const quill = new window.Quill(host, {
    theme: "snow",
    placeholder,
    modules: {
      toolbar: [
        [{ header: [2, 3, false] }],
        ["bold", "italic", "underline"],
        [{ list: "ordered" }, { list: "bullet" }],
        ["blockquote", "code", "code-block"],
        ["link"],
        [{ indent: "-1" }, { indent: "+1" }],
        ["clean"],
      ],
    },
  });
  // Swedish tooltips for the toolbar.
  const titles = {
    "ql-bold": "Fet", "ql-italic": "Kursiv", "ql-underline": "Understruken",
    "ql-blockquote": "Varning", "ql-code": "Värde (kod)", "ql-code-block": "Kodblock",
    "ql-link": "Länk", "ql-clean": "Ta bort formatering",
  };
  host.parentElement.querySelectorAll(".ql-toolbar button").forEach((b) => {
    for (const c of b.classList) if (titles[c]) b.title = titles[c];
    if (b.classList.contains("ql-list")) b.title = b.value === "ordered" ? "Numrerad lista" : "Punktlista";
    if (b.classList.contains("ql-indent")) b.title = b.value === "+1" ? "Öka indrag" : "Minska indrag";
  });
  if (html) quill.clipboard.dangerouslyPasteHTML(html, "silent");
  return quill;
}

// Semantic HTML gives real <ul>/<ol> lists, which the reader view and the Word export expect.
// Quill 2.0.3 writes every space as &nbsp; there, so turn those back into plain spaces.
export function getHtml(quill) {
  return quill.getSemanticHTML().replace(/&nbsp;/g, " ");
}
