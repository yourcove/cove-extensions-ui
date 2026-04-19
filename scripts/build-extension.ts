#!/usr/bin/env node
/**
 * Extension UI Build Script
 * Compiles TypeScript React components into an ESM bundle loadable by Cove.
 * Usage: tsx scripts/build-extension.ts <ExtensionName>
 */

import * as esbuild from "esbuild";
import * as fs from "fs/promises";
import * as path from "path";
import { fileURLToPath } from "url";

const runtimeAliases = {
  react: "@cove/runtime/react",
  "react-dom": "@cove/runtime/react-dom",
  "react-dom/client": "@cove/runtime/react-dom-client",
  "react/jsx-runtime": "@cove/runtime/react-jsx-runtime",
  "react/jsx-dev-runtime": "@cove/runtime/react-jsx-dev-runtime",
  "@tanstack/react-query": "@cove/runtime/react-query",
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

async function build() {
  try {
    await fs.mkdir(distDir, { recursive: true });

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
  } catch (error) {
    console.error(`❌ Failed to build ${extensionName}:`, error);
    process.exit(1);
  }
}

build();
