# p18: fonts

Geist (Latin), Geist Mono (codes, numbers, keys) and Readex Pro (Arabic), the three faces of
direction G, are bundled in `web/public/fonts/` as variable-weight woff2 files, byte for byte the
files the samples use (they come from the `@fontsource-variable` packages). All three are under the
SIL Open Font License 1.1, which the owner accepted for fonts (needs-human #5); each licence text is
beside the file (`OFL-geist.txt`, `OFL-geist-mono.txt`, `OFL-readex-pro.txt`) and each file is listed
with its sha256 in `tests/Gates/font-licences.txt` (the font gate). The design samples' fonts
(`docs/design/samples/fonts/`, 16 files of the six directions) were not listed before and are now,
so the gate reads the whole repository; they are not part of the product.

One stack, `--font-ui: "Bz Geist", "Bz Readex", system-ui, sans-serif`, so a mixed line shares a
baseline; Arabic is one size step larger with looser leading and no letter spacing (`:lang(ar)` in
the tokens). Served from the app's own origin, `font-display: swap`, no network at run time. The
Noto Sans Arabic face of p04 stays for the printed documents of the server (reports module) and as the
web face's former fallback in `styles.css`.
