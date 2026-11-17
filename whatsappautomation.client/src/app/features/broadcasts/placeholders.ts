/** Placeholders the server fills in for each person. "{{name}}" is the standard; "{name}" still works. */
const PLACEHOLDER = /\{\{\s*(name|name_en|name_hi|name_gu|salutation)\s*\}\}|\{(name|name_en|name_hi|name_gu|salutation)\}/gi;

/** "{a|b|c}": one option is picked at random for each person. No braces inside, so it never matches a placeholder. */
const CHOICE = /\{([^{}|]*(?:\|[^{}|]*)+)\}/g;

function escapeHtml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

/**
 * HTML for the preview bubble: placeholders and word choices shown as chips. The text is escaped first, and
 * braces and "|" are not changed by escaping, so the patterns still match.
 */
export function highlightPlaceholders(text: string): string {
  return escapeHtml(text)
    .replace(CHOICE, (_m, options: string) => `<span class="ph">${options.split('|').map((o) => o.trim()).join(' / ')}</span>`)
    .replace(PLACEHOLDER, (_m, a: string | undefined, b: string | undefined) => `<span class="ph">${(a ?? b ?? '').toLowerCase()}</span>`);
}
