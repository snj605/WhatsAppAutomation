/**
 * WhatsApp text formatting as WhatsApp shows it: *bold*, _italic_, ~strike~, ```monospace```, `code` and links.
 * The text is escaped first; only these tags are produced (Angular sanitizes [innerHTML] as well).
 */
export function waFormat(text: string): string {
  const escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  return escaped
    .replace(/```([\s\S]+?)```/g, '<code class="mono">$1</code>')
    .replace(/`([^`\n]+)`/g, '<code>$1</code>')
    .replace(/(^|[\s(])\*(?!\s)([^*\n]+?)\*(?=$|[\s.,!?)])/gm, '$1<strong>$2</strong>')
    .replace(/(^|[\s(])_(?!\s)([^_\n]+?)_(?=$|[\s.,!?)])/gm, '$1<em>$2</em>')
    .replace(/(^|[\s(])~(?!\s)([^~\n]+?)~(?=$|[\s.,!?)])/gm, '$1<s>$2</s>')
    .replace(/\b(https?:\/\/[^\s<]+[^\s<.,!?)])/g, '<a href="$1" target="_blank" rel="noopener noreferrer">$1</a>');
}

/** What kind of WhatsApp message a file becomes. A video over 100 MB goes as a document. */
export function waKind(contentType: string, isBigVideo: boolean): 'image' | 'video' | 'audio' | 'document' {
  if (contentType.startsWith('image/') && !contentType.includes('svg')) return 'image';
  if (contentType.startsWith('video/')) return isBigVideo ? 'document' : 'video';
  if (contentType.startsWith('audio/')) return 'audio';
  return 'document';
}

/** "PDF", "DOCX", … from the file name. */
export function docType(fileName: string): string {
  const ext = fileName.includes('.') ? fileName.split('.').pop()!.toUpperCase() : 'FILE';
  return ext.length <= 5 ? ext : 'FILE';
}

/** The colour of a document's icon, as WhatsApp uses: PDF red, Word blue, Excel green, PowerPoint orange. */
export function docColor(fileName: string): string {
  const t = docType(fileName);
  if (t === 'PDF') return '#e5252a';
  if (['DOC', 'DOCX', 'RTF', 'ODT'].includes(t)) return '#2b579a';
  if (['XLS', 'XLSX', 'CSV', 'ODS'].includes(t)) return '#217346';
  if (['PPT', 'PPTX', 'ODP'].includes(t)) return '#d24726';
  return '#8696a0';
}
