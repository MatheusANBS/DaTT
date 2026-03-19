# DaTT — Database Tool

A cross-platform desktop database client built with .NET 8 and Avalonia UI. Connect to multiple database engines from a single interface — browse schemas, edit data, run queries, compare structures, monitor servers, and export in multiple formats.

---

## Supported Databases

| Engine        | Protocol Prefix                                    |
| ------------- | -------------------------------------------------- |
| PostgreSQL    | `postgresql://`, `postgres://`                     |
| MySQL         | `mysql://`                                         |
| MariaDB       | `mariadb://`                                       |
| Oracle        | `jdbc:oracle:thin:`                                |
| MongoDB       | `mongodb://`, `mongodb+srv`                        |
| Redis         | `redis://`                                         |
| ElasticSearch | `elasticsearch://`, `es://`, `http://`, `https://` |
| Hive          | `jdbc:hive2`                                       |

---

## Features

### Object Explorer

Hierarchical tree view of databases, schemas, tables, views, and stored procedures. Supports lazy-loading of schema children and context menu actions (open, truncate, drop, dump, copy name, show source).

### Data Grid

Paginated table browser with inline cell editing. Tracks modified, inserted, and deleted rows for batch commit. JSON cells display a preview button and open in a dedicated modal editor.

### Query Editor

SQL editor with syntax highlighting, keyword/table/column auto-complete, query history, and configurable result preview (100–5000 rows). Results render in a grid with full export support.

### Schema Diff

Compare two tables side-by-side — columns, indexes, and foreign keys. Generates ALTER TABLE statements to reconcile differences.

### Server Monitor

Real-time health dashboard with ping latency history, connection count, query stats, and engine-specific metrics. Auto-refresh at configurable intervals.

### Export / Dump

- **Table-level**: CSV (`;` separator), JSON, SQL (INSERT), XLSX.
- **Table-level (structure)**: DDL (CREATE TABLE), Data (INSERT), or both.
- **Schema-level**: Dump all tables in a schema — Structure, Data, or both. Outputs one `.sql` file per table.
- Excel export truncates cells at 32 000 characters.

### Create Table

Visual table designer with dynamic column definitions (name, type, nullable, default, primary key). Generates and previews the CREATE TABLE script before execution.

### Redis Console

Execute Redis commands with history, inspect key types and TTL, rename keys, select databases, and view server status metrics.

### ElasticSearch Console

HTTP API console with method selection (GET, POST, PUT, DELETE), path and JSON body editing, index management, and formatted response display.

### SSH Workspace

Remote server management over SSH with SFTP file explorer (upload, download, create), terminal command execution, and port forwarding for tunneling to remote databases.

### Connection Manager

Connection builder with engine-specific fields, SSL/TLS support, SSH tunnel configuration (password or key-based), port forwarding, connection testing, and persistent profiles saved locally.

---

## Tech Stack

| Layer           | Technology                                      |
| --------------- | ----------------------------------------------- |
| Runtime         | .NET 8.0                                        |
| UI              | Avalonia 11.3 (FluentTheme + custom dark theme) |
| MVVM            | CommunityToolkit.Mvvm 8.4                       |
| DI              | Microsoft.Extensions.DependencyInjection        |
| Code Editor     | AvaloniaEdit                                    |
| PostgreSQL      | Npgsql 10.0                                     |
| MySQL / MariaDB | MySqlConnector 2.5                              |
| Oracle          | Oracle.ManagedDataAccess.Core 23.26             |
| MongoDB         | MongoDB.Driver 3.7                              |
| Redis           | StackExchange.Redis 2.9                         |
| SSH / SFTP      | SSH.NET 2024.2                                  |
| Excel Export    | ClosedXML 0.104                                 |

---

## Project Structure

```
DaTT.sln
├── src/
│   ├── DaTT.Core/            # Interfaces, models, services (no UI dependency)
│   │   ├── Interfaces/        # IDatabaseProvider, ISqlDialect, IProviderFactory
│   │   ├── Models/            # ConnectionConfig, ColumnMeta, IndexMeta, ForeignKeyMeta, etc.
│   │   └── Services/         # ConnectionConfigService, SchemaDiffService
│   │
│   ├── DaTT.Providers/       # Database provider implementations
│   │   ├── BaseSqlProvider.cs # Shared SQL logic
│   │   ├── ProviderFactory.cs # Connection-string → provider resolution
│   │   ├── Providers/         # SQL dialect implementations
│   │   └── *Provider.cs       # One file per engine
│   │
│   └── DaTT.App/             # Avalonia desktop application
│       ├── ViewModels/        # MVVM view models
│       ├── Views/             # AXAML views + code-behind
│       ├── Styles/            # GamerTheme.axaml (VS Code dark theme)
│       ├── Infrastructure/    # AppLog
│       └── Assets/            # Icons, images
│
└── tests/
    └── DaTT.Tests/            # Unit tests
```

---

## Build & Run

**Prerequisites**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Clone
git clone https://github.com/<your-user>/DaTT.git
cd DaTT

# Restore & Build
dotnet restore
dotnet build

# Run
dotnet run --project src/DaTT.App
```

---

## Architecture

```
┌─────────────┐     ┌──────────────────┐     ┌─────────────────┐
│  DaTT.App   │ ──► │  DaTT.Core       │ ◄── │ DaTT.Providers  │
│  (Avalonia)  │     │  (Interfaces &   │     │ (8 DB engines)  │
│  ViewModels  │     │   Models)        │     │ BaseSqlProvider  │
│  Views       │     │  Services        │     │ ProviderFactory  │
└─────────────┘     └──────────────────┘     └─────────────────┘
```

- **DaTT.Core** defines contracts (`IDatabaseProvider`, `ISqlDialect`) and data models. No framework dependency.
- **DaTT.Providers** implements one provider per database engine, all resolved via `ProviderFactory` from the connection string scheme.
- **DaTT.App** is the Avalonia UI layer using MVVM with `CommunityToolkit.Mvvm` source generators. A `ViewLocator` maps ViewModels to Views by naming convention.

---

## License

MIT
