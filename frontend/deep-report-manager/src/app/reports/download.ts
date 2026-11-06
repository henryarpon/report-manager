/** File name from a `Content-Disposition` header; prefers the RFC 5987 `filename*=UTF-8''…` form. */
export function fileNameFromDisposition(header: string | null): string | null {
  if (!header) {
    return null;
  }
  const encoded = /filename\*\s*=\s*(?:UTF-8|utf-8)''([^;]+)/.exec(header);
  if (encoded) {
    try {
      return decodeURIComponent(encoded[1].trim().replace(/^"|"$/g, ''));
    } catch {
      // Fall through to the plain form.
    }
  }
  const plain = /filename\s*=\s*"?([^";]+)"?/.exec(header);
  return plain ? plain[1].trim() : null;
}

/** Saves a blob through a temporary object URL. */
export function saveBlob(doc: Document, blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = doc.createElement('a');
  link.href = url;
  link.download = fileName;
  link.style.display = 'none';
  doc.body.appendChild(link);
  link.click();
  link.remove();
  // Give the browser time to start the download before releasing the blob.
  setTimeout(() => URL.revokeObjectURL(url), 10_000);
}
