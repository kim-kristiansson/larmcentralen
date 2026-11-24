// Bundles each TypeScript entry in src/ into the app's wwwroot as minified JavaScript.
// Everything (Milkdown included) is bundled in, so the app needs no CDN and works offline.
// Run with `npm run build` (type-checks first). The output is committed.
import { build } from "esbuild";

const banner = "/* Built from Larmcentralen.Client.UI/scripts/src. Do not edit: change the .ts file and run `npm run build`. */";
const common = { bundle: true, minify: true, target: "es2022", legalComments: "eof", banner: { js: banner }, logLevel: "info" };

// ES modules loaded on demand by the Razor components with import().
await build({
  ...common,
  entryPoints: ["src/theme.ts", "src/combobox.ts", "src/milkdown-interop.ts"],
  outdir: "../wwwroot/js",
  format: "esm",
});

// theme-init runs as a classic <script> in the app's index.html before the first paint, so no module.
await build({
  ...common,
  entryPoints: ["src/theme-init.ts"],
  outfile: "../../Larmcentralen.App/wwwroot/theme-init.js",
  format: "iife",
});
