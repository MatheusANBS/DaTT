<p align="center">
  <img src="icons/DATT/DATT_128x128.png" alt="DaTT" width="80" height="80" />
</p>

<h1 align="center">DaTT — Database Tool</h1>

<p align="center">
  <strong>Cliente de banco de dados desktop multiplataforma com suporte a 8 engines</strong>
</p>

<p align="center">
  <em>Browse schemas, edite dados, execute queries, compare estruturas e monitore servidores — tudo em uma interface</em>
</p>

<p align="center">
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8.0" /></a>
  <a href="https://avaloniaui.net/"><img src="https://img.shields.io/badge/Avalonia-11.3-purple?style=for-the-badge&logo=avalonia&logoColor=white" alt="Avalonia" /></a>
  <a href="https://github.com/CommunityToolkit/dotnet"><img src="https://img.shields.io/badge/CommunityToolkit-MVVM-blue?style=for-the-badge" alt="CommunityToolkit.Mvvm" /></a>
  <a href="https://github.com/MatheusANBS/DaTT/blob/main/LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge" alt="License" /></a>
</p>

<p align="center">
  <a href="https://github.com/MatheusANBS/DaTT/releases"><img src="https://img.shields.io/github/downloads/MatheusANBS/DaTT/total?style=flat-square&color=blue" alt="Total Downloads" /></a>
  <a href="https://github.com/MatheusANBS/DaTT/releases/latest"><img src="https://img.shields.io/github/v/release/MatheusANBS/DaTT?style=flat-square&cacheSeconds=3600" alt="Latest Release" /></a>
  <a href="https://github.com/MatheusANBS/DaTT/actions"><img src="https://img.shields.io/badge/Status-Active-brightgreen?style=flat-square" alt="Status" /></a>
  <a href="https://github.com/MatheusANBS/DaTT/releases/latest"><img src="https://img.shields.io/badge/Windows-10%2F11-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows" /></a>
</p>

---

<p align="center">
  <img src="assets/screenshots/connection-manager.png" alt="DaTT — Connection Manager" width="49%" />
  <img src="assets/screenshots/data-grid.png" alt="DaTT — Data Grid" width="49%" />
</p>

<p align="center">
  <em>Connection Manager com suporte a SSL e SSH Tunnel &bull; Data Grid com edição inline e exportação</em>
</p>

---

## <img src="assets/icons/rocket.svg" width="24" height="24" /> Início Rápido

```bash
1. Baixe o instalador na página de Releases

# https://github.com/MatheusANBS/DaTT/releases/latest

2. Execute DaTT-Setup-x.x.x.exe

Instalação por usuário — não requer admin

3. Pronto! Conecte ao seu banco de dados
```

**Primeira conexão:**

1. Clique em **New Connection** no Object Explorer
2. Selecione o engine, preencha host, porta, usuário e senha
3. Clique em **Test Connection** → **Connect**

---

## <img src="assets/icons/lightbulb.svg" width="24" height="24" /> Por que DaTT?

| <img src="assets/icons/cross.svg" width="16" height="16" /> **Antes**                                                 | <img src="assets/icons/check.svg" width="16" height="16" /> **Agora**                           |
| --------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| <img src="assets/icons/folder.svg" width="16" height="16" /> Uma ferramenta para cada banco                           | <img src="assets/icons/lightning.svg" width="16" height="16" /> **1 app para todos os engines** |
| <img src="assets/icons/refresh.svg" width="16" height="16" /> Trocar entre MySQL Workbench, pgAdmin, Redis Insight... | <img src="assets/icons/target.svg" width="16" height="16" /> **Tudo em abas na mesma janela**   |
| <img src="assets/icons/clock.svg" width="16" height="16" /> Instalar e configurar N clientes                          | <img src="assets/icons/wind.svg" width="16" height="16" /> **Instalação de um clique**          |
| <img src="assets/icons/cross.svg" width="16" height="16" /> Sem comparação de schemas                                 | <img src="assets/icons/check.svg" width="16" height="16" /> **Schema Diff integrado**           |
| <img src="assets/icons/monitor.svg" width="16" height="16" /> Monitoramento externo                                   | <img src="assets/icons/chart-bar.svg" width="16" height="16" /> **Server Monitor embutido**     |

---

## <img src="assets/icons/save.svg" width="24" height="24" /> Engines Suportados

| Engine        | Prefixo de Conexão                                 |
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

## <img src="assets/icons/star.svg" width="24" height="24" /> Funcionalidades em Destaque

### <img src="assets/icons/tree.svg" width="20" height="20" /> Object Explorer

<p align="center">
  <img src="assets/screenshots/object-explorer.png" alt="Object Explorer" width="100%" />
</p>

- Árvore hierárquica: databases, schemas, tabelas, views, procedures, functions, triggers, users
- Lazy-loading dos filhos ao expandir
- Context menu: abrir, truncar, drop, dump, copiar nome, ver source

---

### <img src="assets/icons/files.svg" width="20" height="20" /> Data Grid

<p align="center">
  <img src="assets/screenshots/data-grid-full.png" alt="Data Grid com edição inline" width="100%" />
</p>

- Navegação paginada com tamanho de página configurável
- Edição inline — rastreia linhas modificadas, inseridas e deletadas para commit em lote
- Células JSON com preview e editor modal dedicado
- Filtros, ordenação e exportação: **CSV, JSON, SQL INSERT, XLSX**

---

### <img src="assets/icons/editor.svg" width="20" height="20" /> Query Editor

<p align="center">
  <img src="assets/screenshots/query-editor.png" alt="Query Editor" width="100%" />
</p>

- Syntax highlighting e autocomplete (keywords, tabelas, colunas)
- Executar seleção ou script completo
- Histórico de queries
- Preview de resultados configurável (100–5000 linhas)
- Formatação de SQL

---

### <img src="assets/icons/refresh.svg" width="20" height="20" /> Schema Diff

<p align="center">
  <img src="assets/screenshots/schema-diff.png" alt="Schema Diff" width="100%" />
</p>

- Compara duas tabelas lado a lado — colunas, índices e foreign keys
- Gera scripts `ALTER TABLE` para reconciliar diferenças e permite aplicar direto

---

### <img src="assets/icons/chart-bar.svg" width="20" height="20" /> Server Monitor

<p align="center">
  <img src="assets/screenshots/server-monitor.png" alt="Server Monitor" width="100%" />
</p>

- Dashboard em tempo real: latência ping, contagem de conexões, query stats
- Métricas específicas por engine
- Auto-refresh com intervalo configurável

---

### <img src="assets/icons/monitor.svg" width="20" height="20" /> Redis Console

<p align="center">
  <img src="assets/screenshots/redis-console.png" alt="Redis Console" width="100%" />
</p>

- Execute comandos Redis com histórico
- Inspecione key types, TTL, renomeie keys, selecione databases
- Visualização de métricas do servidor

---

### <img src="assets/icons/globe.svg" width="20" height="20" /> ElasticSearch Console

<p align="center">
  <img src="assets/screenshots/elastic-console.png" alt="ElasticSearch Console" width="100%" />
</p>

- Console HTTP: GET, POST, PUT, DELETE com body JSON
- Gerenciamento de índices
- Resposta formatada com highlighting

---

### <img src="assets/icons/gnometerminal.svg" width="20" height="20" /> SSH Workspace

<p align="center">
  <img src="assets/screenshots/ssh-workspace.png" alt="SSH Workspace" width="100%" />
</p>

- File explorer remoto via SFTP (upload, download, criar pastas)
- Execução de comandos via terminal SSH
- Port forwarding para tunelamento a bancos remotos

---

### <img src="assets/icons/download.svg" width="20" height="20" /> Auto-Update

- <img src="assets/icons/check.svg" width="16" height="16" /> Detecta nova versão no GitHub Releases ao iniciar
- <img src="assets/icons/check.svg" width="16" height="16" /> Indicador visual na barra inferior quando há atualização
- <img src="assets/icons/check.svg" width="16" height="16" /> Download com barra de progresso em tempo real
- <img src="assets/icons/check.svg" width="16" height="16" /> Instalação silenciosa — sem precisar de admin
- <img src="assets/icons/check.svg" width="16" height="16" /> App fecha e reabre automaticamente atualizado

---

## <img src="assets/icons/keyboard.svg" width="24" height="24" /> Atalhos de Teclado

<details>
<summary><strong>Clique para ver todos os atalhos</strong></summary>

| Atalho       | Ação             | Contexto     |
| ------------ | ---------------- | ------------ |
| `Ctrl+T`     | Nova aba         | Global       |
| `Ctrl+W`     | Fechar aba       | Global       |
| `F5`         | Executar query   | Query Editor |
| `Ctrl+Enter` | Executar seleção | Query Editor |
| `Ctrl+Space` | Autocomplete     | Query Editor |
| `Ctrl+E`     | Exportar         | Data Grid    |
| `Ctrl+R`     | Refresh          | Data Grid    |
| `Delete`     | Deletar linha    | Data Grid    |

</details>

---

## <img src="assets/icons/dotnet.svg" width="24" height="24" /> Tech Stack

```
┌─────────────────────────────────────────────────────────┐
│                        DaTT                             │
├─────────────────────────────────────────────────────────┤
│  UI Layer                                               │
│  ┌───────────────┐  ┌──────────────┐  ┌──────────────┐  │
│  │ Avalonia 11.3 │  │ AvaloniaEdit │  │ GamerTheme   │  │
│  │ (FluentTheme) │  │ (Code Editor)│  │ (VS Code dark│  │
│  └───────────────┘  └──────────────┘  └──────────────┘  │
├─────────────────────────────────────────────────────────┤
│  MVVM Layer                                             │
│  ┌─────────────────────────────────────────────────────┐│
│  │       CommunityToolkit.Mvvm 8.4                     ││
│  │   (ObservableObject, RelayCommand, generators)      ││
│  └─────────────────────────────────────────────────────┘│
├─────────────────────────────────────────────────────────┤
│  Providers                                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐  │
│  │ Npgsql   │ │MySqlConn.│ │ Oracle   │ │ MongoDB    │  │
│  └──────────┘ └──────────┘ └──────────┘ └────────────┘  │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────────┐  │
│  │ SE.Redis │ │ SSH.NET  │ │ClosedXML │ │ HttpClient │  │
│  └──────────┘ └──────────┘ └──────────┘ └────────────┘  │
├─────────────────────────────────────────────────────────┤
│  .NET 8.0                                               │
└─────────────────────────────────────────────────────────┘
```

| Camada          | Tecnologia                               |
| --------------- | ---------------------------------------- |
| Runtime         | .NET 8.0                                 |
| UI              | Avalonia 11.3                            |
| MVVM            | CommunityToolkit.Mvvm 8.4                |
| DI              | Microsoft.Extensions.DependencyInjection |
| Code Editor     | AvaloniaEdit                             |
| PostgreSQL      | Npgsql 10.0                              |
| MySQL / MariaDB | MySqlConnector 2.5                       |
| Oracle          | Oracle.ManagedDataAccess.Core 23.26      |
| MongoDB         | MongoDB.Driver 3.7                       |
| Redis           | StackExchange.Redis 2.9                  |
| SSH / SFTP      | SSH.NET 2024.2                           |
| Excel Export    | ClosedXML 0.104                          |

---

## <img src="assets/icons/lock.svg" width="24" height="24" /> Segurança

<table>
<tr>
<td width="50%">

### <img src="assets/icons/key.svg" width="16" height="16" /> Credenciais

- <img src="assets/icons/check.svg" width="16" height="16" /> Senhas nunca em texto plano no disco
- <img src="assets/icons/check.svg" width="16" height="16" /> Perfis armazenados localmente no AppData
- <img src="assets/icons/check.svg" width="16" height="16" /> Sem telemetria, sem tracking

</td>
<td width="50%">

### <img src="assets/icons/key.svg" width="16" height="16" /> Autenticação SSH

- <img src="assets/icons/check.svg" width="16" height="16" /> Senha tradicional
- <img src="assets/icons/check.svg" width="16" height="16" /> Chave privada (PEM)
- <img src="assets/icons/check.svg" width="16" height="16" /> Suporte a passphrase
- <img src="assets/icons/check.svg" width="16" height="16" /> Port forwarding para túneis seguros

</td>
</tr>
</table>

---

## <img src="assets/icons/folder.svg" width="24" height="24" /> Estrutura do Projeto

```
DaTT.sln
├── src/
│   ├── DaTT.Core/            # Interfaces, models, services (sem UI)
│   │   ├── Interfaces/        # IDatabaseProvider, ISqlDialect, IProviderFactory
│   │   ├── Models/            # ConnectionConfig, ColumnMeta, IndexMeta...
│   │   └── Services/          # ConnectionConfigService, SchemaDiffService
│   │
│   ├── DaTT.Providers/        # Implementações por engine
│   │   ├── BaseSqlProvider.cs # Lógica SQL compartilhada
│   │   ├── ProviderFactory.cs # Connection-string → provider
│   │   └── *Provider.cs       # Um arquivo por engine
│   │
│   └── DaTT.App/              # Aplicação Avalonia
│       ├── ViewModels/        # MVVM view models
│       ├── Views/             # AXAML views + code-behind
│       ├── Styles/            # GamerTheme.axaml
│       ├── Infrastructure/    # AppLog, UpdateService
│       └── Assets/            # Icons, imagens
│
└── tests/
    └── DaTT.Tests/            # Testes unitários
```

---

## <img src="assets/icons/wrench.svg" width="24" height="24" /> Build & Run

**Prerequisito**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
# Clone
git clone https://github.com/MatheusANBS/DaTT.git
cd DaTT

# Restore & Build
dotnet restore
dotnet build

# Run
dotnet run --project src/DaTT.App
```

---

## <img src="assets/icons/heart.svg" width="24" height="24" /> Contribuição

Contribuições são bem-vindas! Abra uma issue ou pull request.

---

## <img src="assets/icons/note.svg" width="24" height="24" /> License

MIT

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
