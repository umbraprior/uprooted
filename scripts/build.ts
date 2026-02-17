/**
 * Build script -- Uses esbuild to bundle Uprooted into two output files:
 *   - dist/uprooted-preload.js  (IIFE bundle injected into Root)
 *   - dist/uprooted.css         (combined CSS from all built-in plugins)
 */

import * as esbuild from "esbuild";
import fs from "node:fs";
import path from "node:path";

const ROOT = path.resolve(import.meta.dirname, "..");
const DIST = path.join(ROOT, "dist");
const SRC = path.join(ROOT, "src");

const isWatch = process.argv.includes("--watch");

// Collect CSS from built-in plugins
function collectPluginCss(): string {
  const cssFiles: string[] = [];
  const pluginsDir = path.join(SRC, "plugins");

  function walk(dir: string): void {
    if (!fs.existsSync(dir)) return;
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, entry.name);
      if (entry.isDirectory()) {
        walk(full);
      } else if (entry.name.endsWith(".css")) {
        cssFiles.push(full);
      }
    }
  }

  walk(pluginsDir);

  return cssFiles
    .map((f) => `/* ${path.relative(ROOT, f)} */\n${fs.readFileSync(f, "utf-8")}`)
    .join("\n\n");
}

async function build(): Promise<void> {
  fs.mkdirSync(DIST, { recursive: true });

  // Bundle the preload entry point
  const ctx = await esbuild.context({
    entryPoints: [path.join(SRC, "core", "preload.ts")],
    bundle: true,
    format: "iife",
    globalName: "Uprooted",
    outfile: path.join(DIST, "uprooted-preload.js"),
    platform: "browser",
    target: "chrome120",
    sourcemap: true,
    define: {
      __UPROOTED_VERSION__: JSON.stringify(
        JSON.parse(fs.readFileSync(path.join(ROOT, "package.json"), "utf-8")).version,
      ),
    },
    // Node builtins should not be bundled (they're only used in CLI scripts)
    external: ["node:fs", "node:path"],
  });

  if (isWatch) {
    await ctx.watch();
    console.log("[build] Watching for changes...");
  } else {
    await ctx.rebuild();
    await ctx.dispose();
    console.log("[build] Built dist/uprooted-preload.js");
  }

  // Write combined CSS
  const css = collectPluginCss();
  fs.writeFileSync(path.join(DIST, "uprooted.css"), css, "utf-8");
  console.log("[build] Built dist/uprooted.css");
}

build().catch((err) => {
  console.error(err);
  process.exit(1);
});
