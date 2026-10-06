# p06 — Printed documents use the screens' own strings

Date: 2026-10-04. Piece: p06-form-report. Status: accepted.

## Decision

The reports module embeds the web app's string files (`web/src/modules/*/i18n/{en,ar}.json`) as
resources (`web-strings/…`) and labels every report, column, parameter, group count and footer
from them. A report definition names string keys (`LabelKey`), never literal text. The string
gate (`StringGateTests`) fails when a report's key is missing from the web strings, so a label
exists in English and Arabic before it can print.

The build fails if the string files are missing (`RequireWebStrings` target in the reports
project). The container build copies only the string files into the .NET build stage (a
`strings` stage in the `Dockerfile`), so a change to a screen's code does not rebuild the server.

## Why

A column printed as "Legal name (Arabic)" while the screen says "Arabic legal name" is two
products. One set of strings keeps print, export and screen identical in both languages, and the
web check (`npm run check`) already holds English and Arabic to parity, plural forms included.
Copying strings into server resource files would have needed a second parity check and would
drift.
