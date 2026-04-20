#!/usr/bin/env node
/**
 * Extension UI Build Script
 * Compiles TypeScript React components into an ESM bundle + Tailwind CSS bundle.
 * Usage: tsx scripts/build-extension.ts <ExtensionName>
 */

import * as esbuild from "esbuild";
import * as fs from "fs/promises";
import * as path from "path";
import { fileURLToPath } from "url";
import { exec } from "child_process";
import { promisify } from "util";

const execAsync = promisify(exec);

const runtimeAliases = {
  react: "@cove/runtime/react",
  "react-dom": "@cove/runtime/react-dom",
  "react-dom/client": "@cove/runtime/react-dom-client",
  "react/jsx-runtime": "@cove/runtime/react-jsx-runtime",
  "react/jsx-dev-runtime": "@cove/runtime/react-jsx-dev-runtime",
  "@tanstack/react-query": "@cove/runtime/react-query",
  "lucide-react": "@cove/runtime/lucide-react",
  "@cove/runtime/components": "@cove/runtime/components",
};

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.join(__dirname, "..");
const extensionName = process.argv[2];

if (!extensionName) {
  console.error("Usage: tsx scripts/build-extension.ts <ExtensionName>");
  process.exit(1);
}

const extensionDir = path.join(rootDir, "extensions", extensionName);
const uiDir = path.join(extensionDir, "ui");
const distDir = path.join(extensionDir, "dist");
const entryFile = path.join(uiDir, `${extensionName}.tsx`);
const outFile = path.join(distDir, "bundle.js");
const cssOutFile = path.join(distDir, "bundle.css");
const sharedTheme = path.join(rootDir, "shared", "theme.css");

async function buildJs() {
  await esbuild.build({
    entryPoints: [entryFile],
    bundle: true,
    format: "esm",
    outfile: outFile,
    alias: runtimeAliases,
    external: Object.values(runtimeAliases),
    jsx: "automatic",
    jsxImportSource: "react",
    splitting: false,
    sourcemap: false,
    minify: true,
  });
  console.log(`✅ Built ${extensionName} UI bundle: ${outFile}`);
}

async function buildCss() {
  // Generate a temporary CSS entry that imports the shared theme
  // and uses @source to scan this extension's UI directory
  const tmpCss = path.join(distDir, "_entry.css");
  const relativeUiDir = path.relative(distDir, uiDir).replace(/\\/g, "/");
  const relativeTheme = path.relative(distDir, sharedTheme).replace(/\\/g, "/");

  await fs.writeFile(
    tmpCss,
    `@import "${relativeTheme}";\n@source "${relativeUiDir}";\n`
  );

  try {
    const inputArg = JSON.stringify(tmpCss);
    const outputArg = JSON.stringify(cssOutFile);
    await execAsync(`npx tailwindcss --input ${inputArg} --output ${outputArg} --minify`, {
      cwd: rootDir,
    });
    console.log(`✅ Built ${extensionName} CSS bundle: ${cssOutFile}`);
  } finally {
    await fs.unlink(tmpCss).catch(() => {});
  }
}

async function build() {
  try {
    await fs.mkdir(distDir, { recursive: true });
    await Promise.all([buildJs(), buildCss()]);
  } catch (error) {
    console.error(`❌ Failed to build ${extensionName}:`, error);
    process.exit(1);
  }
}

build();
