// Bundles tests/*.test.ts with esbuild into .test-out (git-ignored) and runs them with node:test.
import { build } from "esbuild";
import { readdirSync } from "node:fs";
import { spawnSync } from "node:child_process";

const entries = readdirSync("tests").filter((f) => f.endsWith(".test.ts")).map((f) => `tests/${f}`);
await build({ entryPoints: entries, bundle: true, platform: "node", format: "esm", outdir: ".test-out", outExtension: { ".js": ".mjs" }, external: ["jsdom"], logLevel: "warning" });
const outputs = entries.map((e) => `.test-out/${e.slice("tests/".length).replace(/\.ts$/, ".mjs")}`);
const result = spawnSync(process.execPath, ["--test", ...outputs], { stdio: "inherit" });
process.exit(result.status ?? 1);
