# WinServerMonitor

Webová aplikace (.NET 9, Blazor Server) pro plánování, spouštění a sledování tasků rozdělených podle domén
a pro sledování vytížení serveru (CPU, RAM, diskové IO).

## Funkce

- **Tasky podle domén** – každý task patří do domény (Finance, Sklad, IT…), podle ní se filtruje a seskupuje.
- **Typy tasků**
  - `DbProcedure` – volání uložené procedury/funkce v **PostgreSQL** (`CALL` / `SELECT * FROM`) nebo **SQL Serveru** (`EXEC`).
    `RAISE NOTICE` / `PRINT` a první řádky výsledků jdou do logu.
  - `DbScript` – libovolný SQL skript (SQL Server podporuje oddělovač `GO`).
  - `PowerShell` – inline skript (Windows PowerShell 5.1 nebo pwsh).
  - `Process` – spuštění programu / bat souboru, výstup do logu, konfigurovatelné úspěšné návratové kódy.
  - `Http` – volání HTTP endpointu s kontrolou návratového kódu.
  - `Demo` – simulace práce pro vyzkoušení.
- **Plánování** cron výrazy (5 polí, nebo 6 se sekundami), volitelné časové pásmo, náhled dalších spuštění.
- **Kanban** – sloupce Naplánováno → Ve frontě → Běží → Dokončeno / Selhalo / Zrušeno, filtr domény,
  volitelně řádky (swimlany) po doménách, živé aktualizace.
- **Logy** každého běhu živě v prohlížeči, filtr úrovně, hledání, stažení jako `.txt`.
- **Znovuspuštění** dokončeného běhu (vznikne nový běh navázaný na původní), **zrušení** naplánovaného,
  čekajícího i běžícího běhu, **automatické opakování** při chybě s prodlevou, timeout.
- **Monitoring serveru** – CPU, RAM, propustnost a IOPS disků, fronta disku, zaplnění disků, top procesy;
  grafy za posledních 15 minut s tooltipem.
- Volitelně **Windows autentizace** (Negotiate/Kerberos) a běh jako **Windows služba**.

## Architektura

```
src/
  WinServerMonitor.Core            doména (TaskDefinition, TaskRun, TaskLogEntry) a abstrakce (ITaskExecutor, IServerMetricsProvider)
  WinServerMonitor.Infrastructure  EF Core + PostgreSQL, plánovač, fronta a workery, executory, sběr metrik
  WinServerMonitor.Web             Blazor Server UI
tests/
  WinServerMonitor.Tests           unit testy + integrační testy proti PostgreSQL
```

Průběh běhu:

1. `TaskSchedulerService` (každých 5 s) drží pro každý aktivní task s cronem jeden běh ve stavu **Scheduled**
   s časem dalšího výskytu. Když nastane jeho čas, přepne ho na **Queued** a vloží do fronty.
2. `TaskWorkerService` spouští `MaxConcurrentRuns` workerů. Worker atomicky převezme běh (**Running**),
   najde `ITaskExecutor` podle typu tasku a spustí ho s timeoutem.
3. Logy se zapisují dávkově do tabulky `TaskLogs` a zároveň se posílají do otevřených stránek.
4. Výsledek: **Succeeded / Failed / Cancelled**. Při chybě a `MaxRetries > 0` vznikne nový běh
   (Scheduled, trigger `Retry`) s prodlevou `RetryDelaySeconds`.
5. Po restartu služby se přerušené běhy označí jako Failed a čekající se znovu zařadí do fronty.

Pokud task nemá povolené souběžné běhy, další běh čeká ve frontě, dokud předchozí neskončí.

## Požadavky

- .NET 9 SDK (runtime pro nasazení)
- PostgreSQL 13+ (aplikace si vytvoří vlastní schéma `monitor`, takže může sdílet existující databázi)

## Konfigurace (`appsettings.json`)

```jsonc
{
  "ConnectionStrings": {
    // databáze aplikace (definice tasků, běhy, logy) - tabulky ve schématu "monitor"
    "MonitorDb": "Host=localhost;Port=5432;Database=winservermonitor;Username=monitor;Password=***"
  },
  "Authentication": {
    "WindowsAuthentication": false,  // true = přihlášení účtem Windows (Negotiate)
    "RequiredRole": ""               // např. "DOMENA\\SkupinaAdminu"
  },
  "WinServerMonitor": {
    "MaxConcurrentRuns": 4,
    "SchedulerIntervalSeconds": 5,
    "MetricsIntervalSeconds": 2,
    "MetricsHistoryMinutes": 15,
    "TopProcessCount": 10,
    "RunRetentionDays": 30,          // starší dokončené běhy a jejich logy se mažou; 0 = nikdy
    "SeedDemoTasks": true,           // do prázdné DB vloží ukázkové Demo tasky
    "Connections": {                 // pojmenovaná připojení pro DbProcedure / DbScript tasky
      "Main":   { "Provider": "PostgreSql", "ConnectionString": "Host=...;Database=erp;Username=...;Password=..." },
      "Legacy": { "Provider": "SqlServer",  "ConnectionString": "Server=...;Database=...;Integrated Security=true;TrustServerCertificate=true" }
    }
  }
}
```

Tasky odkazují jen na **název** připojení – hesla tak nejsou v databázi ani v UI. Citlivé hodnoty je vhodné
dát do proměnných prostředí (např. `ConnectionStrings__MonitorDb`, `WinServerMonitor__Connections__Main__ConnectionString`)
nebo do `appsettings.Production.json` mimo repozitář.

Databázové schéma se vytvoří/aktualizuje automaticky při startu (EF Core migrace).

### Parametry procedur (PostgreSQL)

Parametry se zadávají jako JSON objekt a předávají se **pojmenovaně**:

```json
{ "p_date": "2026-09-30", "p_limit": 100 }
```

vygeneruje `CALL public.daily_close(p_date => @p_date, p_limit => @p_limit)`. Hodnoty se posílají jako netypové
literály, PostgreSQL si typ odvodí ze signatury procedury (`date`, `integer`, `numeric`, `boolean`…).
Pro funkce vracející tabulku zvolte „Typ objektu" = `Function`.

## Spuštění pro vývoj

```bash
dotnet run --project src/WinServerMonitor.Web
# http://localhost:5045
```

## Nasazení jako Windows služba

```powershell
dotnet publish src/WinServerMonitor.Web -c Release -r win-x64 --self-contained false -o C:\Services\WinServerMonitor

sc.exe create WinServerMonitor binPath= "C:\Services\WinServerMonitor\WinServerMonitor.Web.exe --urls http://0.0.0.0:5045" start= auto
sc.exe start WinServerMonitor
```

Služba běží pod účtem, pod kterým se spouští i PowerShell/Process tasky a (při `Integrated Security`) připojení
k SQL Serveru – zvolte účet s odpovídajícími právy. Pro čtení výkonových čítačů musí být účet členem skupiny
*Performance Monitor Users* (nebo lokální administrátor).

> Aplikace umí spouštět skripty a procedury – v produkci zapněte `Authentication:WindowsAuthentication`
> a omezte přístup skupinou v `RequiredRole`.

## Testy

```bash
dotnet test
```

Integrační testy potřebují PostgreSQL; výchozí připojení je `Host=localhost;Username=postgres;Password=postgres`,
jiné lze nastavit proměnnou `WSM_TEST_POSTGRES`. Pro každý běh testů se vytvoří dočasná databáze, která se na konci smaže.
Když PostgreSQL není dostupný, integrační testy se přeskočí.

## Přidání vlastního typu tasku

1. Implementujte `ITaskExecutor` (Infrastructure/Executors) – `TaskType`, popis, seznam parametrů pro formulář
   a `ExecuteAsync`, kde logujete přes `context.Log` a respektujete `CancellationToken` (zrušení / timeout).
2. Zaregistrujte ho v `DependencyInjection.AddWinServerMonitor`:
   `services.AddSingleton<ITaskExecutor, MujExecutor>();`

Formulář v UI se vygeneruje automaticky z `Parameters`.

## Migrace databáze

```bash
dotnet tool restore
dotnet ef migrations add <Nazev> -p src/WinServerMonitor.Infrastructure -s src/WinServerMonitor.Infrastructure -o Persistence/Migrations
```
