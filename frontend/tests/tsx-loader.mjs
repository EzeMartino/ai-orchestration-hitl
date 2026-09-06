import { readFile } from "node:fs/promises";
import ts from "typescript";

export async function resolve(specifier, context, nextResolve) {
  try {
    return await nextResolve(specifier, context);
  } catch (error) {
    if (!specifier.startsWith(".") || /\.[cm]?[jt]sx?$/.test(specifier)) {
      throw error;
    }

    for (const extension of [".ts", ".tsx"]) {
      try {
        return await nextResolve(`${specifier}${extension}`, context);
      } catch {
        continue;
      }
    }

    throw error;
  }
}

export async function load(url, context, nextLoad) {
  if (!url.endsWith(".tsx")) {
    return nextLoad(url, context);
  }

  const source = await readFile(new URL(url), "utf8");
  const result = ts.transpileModule(source, {
    compilerOptions: {
      jsx: ts.JsxEmit.ReactJSX,
      module: ts.ModuleKind.ESNext,
      target: ts.ScriptTarget.ES2023,
      verbatimModuleSyntax: true,
    },
  });

  return {
    format: "module",
    source: result.outputText,
    shortCircuit: true,
  };
}
