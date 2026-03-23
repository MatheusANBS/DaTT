## [1.4.1] - 2026-03-21

### Changed

- **Server Monitor — Chart Background** — Fixed transparent background on the latency chart; now uses the panel surface color for a consistent look.
- **Server Monitor — Active Connections Count** — The "CONEXÕES ATIVAS" counter now reflects only truly active sessions (`active`, `idle in transaction`, etc.), excluding idle/sleep pool connections that have no real user behind them.
- **Main Window** — Application now opens maximized by default.

## [1.4.0] - 2026-03-20

### Added

- **Material Icons** — Added Material Icons package to the project and included icon styles in App.axaml.
- **GamerTheme.axaml** — Updated with new styles for DataGrid and TreeView components.
- **DataGridTabView** — Enhanced with filtering capabilities and integrated Material Icons for toolbar buttons.
- **ConnectionManagerView** — Improved with double-tap functionality for connections.
- **CreateTableWindow** — Modified to use Material Icons for action buttons.
- **MainWindow** and **ObjectExplorerViewModel** — Updated to utilize Material Icons for better visual representation.
- **EditRowViewModel** — Refactored to support read-only fields for primary keys during editing.

## [1.3.1] - 2026-03-20

### Added

- **DataGrid** — Visual improvements to table columns and date/time editing

## [1.3.0] - 2026-03-20

### Testing

- **Testing AutoUpdate** — Simulated update check and notification for testing purposes

## [1.2.9] - 2026-03-20

### Added

- **Testing AutoUpdate** — Simulated update check and notification for testing purposes

## [1.2.8] - 2026-03-20

### Hotfixes

- **AutoUpdate** — Fixed update not calling after app ready notification

## [1.2.7] - 2026-03-20

### Added

- **Testing AutoUpdate** — Simulated update check and notification for testing purposes

## [1.2.6] - 2026-03-20

### Hotfixes

- **AutoUpdate** — Tested update check and notification for testing purposes

-

## [1.2.5] - 2026-03-20

### Hotfixes

- **AutoUpdate** — Tested update check and notification for testing purposes

## [1.2.4] - 2026-03-20

### Changed

- **AutoUpdate** — Update check no longer uses a hardcoded delay; now waits asynchronously for the app to finish loading before checking for updates

## [1.2.3] - 2026-03-20

### Hotfixes

- **AutoUpdate Restart Hotfix** — Fixed an issue where DaTT did not relaunch after a silent in-app update.

## [1.2.2] - 2026-03-20

### Testing

- **AutoUpdate** — Simulated update check and notification for testing purposes

## [1.2.1] - 2026-03-20

### Hotfixes

- **AutoUpdate** — Fixed issue with update not working as intended.

## [1.2.0] - 2026-03-20

### Added

- **Testing AutoUpdate** — Simulated update check and notification for testing purposes

## [1.1.0] - 2026-03-20

### Added

- **AutoUpdater** — Automatic update checks for new versions with in-app notifications

## [1.0.0] - 2026-03-20

### Initial Release

- Support for **MySQL**, **MariaDB**, **PostgreSQL**, **Oracle**, **MongoDB**, **Hive**, **Redis** and **ElasticSearch**
- **Object Explorer** — browse schemas, tables, views, procedures, functions, triggers and users
- **Data Grid** — paginated browsing, inline editing, insert/delete rows, filter, sort, export (CSV/JSON/SQL/XLSX) and import SQL
- **Query Editor** — syntax highlighting, run selection or full script, formatting and execution history
- **Schema Diff** — compare two tables and generate/apply ALTER scripts
- **Server Monitor** — real-time metrics with auto-refresh
- **Redis Console** — execute raw commands with key browser
- **ElasticSearch Console** — HTTP request builder with index browser
- **SSH Workspace** — remote file browser with upload/download
- **Table Designer** — visual CREATE TABLE wizard with live DDL preview
- Windows installer — per-user install, no admin required
