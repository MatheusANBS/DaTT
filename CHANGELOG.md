# Changelog

---

## [1.4.2] - 2026-03-23

### Added

- **Cell Edit — JSON Format Toggle** — JSON fields now show Flat/Vertical radio buttons in the toolbar; switching reformats the content immediately. The correct format is also applied on initial open.
- **Cell Edit / Cell Expand — Scroll Containment** — Scrolling inside a JSON/text field no longer propagates to the window, preventing the editor from losing focus off-screen.

### Changed

- **Edit Row — Text Field Height** — `TEXT`/`LONGTEXT`/`CLOB` fields now use a taller input area (150px max); `JSON`/`JSONB` fields use 220px max. Regular fields remain at 80px.
- **Edit Row — Column Label Wrapping** — Column name and data type labels now wrap instead of being clipped when names are long.
