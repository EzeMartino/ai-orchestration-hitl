const hookUrlSuffix = "/src/hooks/useAnalysisSession.ts";

export async function resolve(specifier, context, nextResolve) {
  const fromHook = context.parentURL?.replaceAll("\\", "/").endsWith(hookUrlSuffix);

  if (fromHook && specifier === "react") {
    return {
      url: new URL("./hookRuntime.mjs", import.meta.url).href,
      shortCircuit: true,
    };
  }

  if (fromHook && specifier === "../services/api") {
    return {
      url: new URL("./analysisSessionApiStub.mjs", import.meta.url).href,
      shortCircuit: true,
    };
  }

  if (fromHook && specifier === "../utils/financialMetricsReview") {
    return nextResolve(`${specifier}.ts`, context);
  }

  return nextResolve(specifier, context);
}
