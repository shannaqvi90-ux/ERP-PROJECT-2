# p11 — What the server accepts, and how it says so

Date: 2026-11-09. Piece: p11-attachments, round 1. Status: accepted.

- **Body.** One file per request: the body is the file's bytes (any request content type), the `X-File-Name` header (percent-encoded UTF-8) is its name. A header, not a query parameter: the account-takeover gate refuses a state-changing endpoint with a documented query value that none of its checks aims at a stronger user, and the name is only text the server cleans. Several files are several
  requests, which the screen sends three at a time. Raw bytes avoid a multipart parser and its antiforgery rules, stream straight into
  the store and let the request be counted against the limit as it arrives.
- **Size.** 20 MB a file (`AttachmentLimits.MaxFileBytes`, inside the web server's default body limit), checked from `Content-Length` and again
  while streaming (a chunked body cannot slip past); 413 `attachmentsTooLarge`. 100 files a record; 409 `attachmentsTooMany`.
  An empty file is 400.
- **Type.** Allow-list by extension: pdf, png, jpg/jpeg, gif, webp, txt, csv, doc, xls, ppt, docx, xlsx, pptx. Then the bytes must be of that
  kind: signatures for pdf, png, jpeg, gif, webp and legacy Office; UTF-8 without control characters for txt and csv; a zip with
  `[Content_Types].xml` and a `word/`, `xl/` or `ppt/` part for the Open XML kinds. 415 `attachmentsType` (extension) or
  `attachmentsContent` (bytes). The content type served is the list's, never the browser's. Scripts, pages, SVG, archives and
  executables are not accepted.
- **Serving.** `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`. A download is `Content-Disposition: attachment`
  with a safely encoded name. Only images and PDF are shown in the page (`?inline=true`): an image with `Content-Security-Policy:
  default-src 'none'; sandbox`; a PDF with `frame-ancestors 'self'` and `X-Frame-Options: SAMEORIGIN` so the screen can frame it
  (the application's own headers forbid framing everything else).
- **Client checks** (size, empty, extension) are a courtesy that saves the round trip; the server repeats every check.
- **Errors and strings.** Problem codes `attachmentsName`, `attachmentsType`, `attachmentsContent`, `attachmentsEmpty`, `attachmentsTooLarge`,
  `attachmentsTooMany`, with English and Arabic texts in the module's resource files; the screen shows the server's title in the
  screen's language.
