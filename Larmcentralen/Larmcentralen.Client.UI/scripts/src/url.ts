// Which addresses an åtgärd may link to or show. Mirrors SolutionContent.IsAllowedUrl / IsVideoUrl in C#.

/** http, https and relative links only; javascript:, data:, protocol-relative "//host" and the rest are refused. */
export function isAllowedUrl(url: string): boolean {
  const trimmed = url.trim();
  if (!trimmed) return false;
  // Browsers ignore tabs, newlines and control characters inside a scheme ("java\tscript:").
  const compact = trimmed.replace(/[\x00-\x20\x7f-\x9f]/g, "");
  const scheme = /^([a-z][a-z0-9+.-]*):/i.exec(compact)?.[1]?.toLowerCase();
  if (scheme) return scheme === "http" || scheme === "https";
  return !compact.startsWith("//") && !compact.startsWith("\\\\") && !compact.startsWith("/\\") && !compact.startsWith("\\/");
}

/** A URL whose path ends in .mp4 or .webm (query and fragment ignored). */
export function isVideoUrl(url: string): boolean {
  const path = url.split(/[?#]/)[0] ?? "";
  return /\.(mp4|webm)$/i.test(path);
}
