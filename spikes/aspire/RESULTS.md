# Aspire integration spike: results

Recorded 2026-11-13 on the development machine: Windows 11, SDK 11.0.100, Docker 29.9.3 with Linux containers, Aspire 13.6.5 (the latest), Microsoft.Data.SqlClient 7.1.2, Playwright for .NET 1.64.1, xUnit v3 4.0.3. The SQL Server image `mcr.microsoft.com/mssql/server:2022-latest` was already on the machine and was never pulled again (`WithImagePullPolicy(ImagePullPolicy.Missing)`).

## What was built

| Part | Role |
|---|---|
| `Notes.AppHost` | Aspire AppHost (`Aspire.AppHost.Sdk/13.6.5`). It runs:<ul><li>a SQL Server container, plus the `notes` database;</li><li>the React notes app from `../react/React.App` in loopback mode, as a project resource;</li><li>Vite as an `AddViteApp` resource (`WithNpm(install: false)`, so no `npm install`).</li></ul>Aspire assigns the app's port (`SPAR_LOOPBACK_PORT`, not proxied) and token (`SPAR_LOOPBACK_TOKEN`, from a secret parameter). It passes `ConnectionStrings__notes` to the app, and the backend address and token to Vite. |
| `Notes.IntegrationTests` | One xUnit test using `DistributedApplicationTestingBuilder`. It starts the whole AppHost and waits until the database is healthy and Vite answers. Playwright then saves a note through the real UI, and the test reads the row back from SQL Server with the connection string Aspire gave the app. Resource logs are kept in `artifacts/aspire/resource-logs.txt`. |
| App changes (`spikes/react`) | A note store interface: in memory by default, SQL Server (plain ADO.NET) when `ConnectionStrings__notes` is set (Debug only, see SqlClient below).<br>The loopback host and the dev server accept their port, token and URL from the environment.<br>`vite.config.ts` accepts the backend from the environment. |

## Results (observed)

The test passed in 3 consecutive runs. Aspire removed the containers at the end every time.

| Phase (seconds from test start) | Run 1 | Run 2 | Run 3 |
|---|---:|---:|---:|
| `StartAsync` returned; the app waits for the database, so this includes SQL Server becoming healthy | 27.2 | 22.7 | 22.7 |
| Vite serving | 28.7 | 24.1 | 24.4 |
| UI journey done (load, save, see it listed) | 30.6 | 25.4 | 25.7 |
| Row verified in SQL Server | 30.6 | 25.4 | 25.7 |
| Whole `dotnet test`, built already | 48 s | 42 s | 43 s |

**Observations**

- **SQL Server dominates.** About 20 s of each run is the container starting. During that time, Aspire's health probes log transient "pre-login handshake" errors (9 lines per run): noise, not failures.
- **`localhost` against `127.0.0.1`.** The first run failed: every bridge call from the page hung. Aspire names endpoints `localhost`, which Node resolves to `::1`, while the loopback backend listens on 127.0.0.1 only and checks its own origin. Normalising the assigned backend URL to 127.0.0.1 fixed it. The loopback host should either listen on both addresses or publish the address it actually listens on.
- **SqlClient requires `InvariantGlobalization=false`.** With it on, which is the default for this app, `SqlConnection.Open` throws `NotSupportedException`, even under JIT.
- **Audit errors.**
  - The NuGet cache had Aspire 13.1.2, which NuGet audit flags: `Aspire.Hosting` (high, GHSA-f7vh-h5g3-7jrh) and MessagePack. Moving to 13.6.5 cleared them and downloaded about 87 MB, mostly the dashboard and the orchestrator.
  - SqlClient 7.1.0 pulled `System.Security.Cryptography.Pkcs` 9.0.18, which is flagged; 10.0.12 is flagged too. SqlClient 7.1.2 depends on Pkcs 9.0.21 and passes the audit with no pin.
- **Build warning:** `ASPIRE010`, "AspireUseCliBundle=false. Some Aspire features require the Aspire CLI bundle". The AppHost was built without the Aspire CLI, and nothing tested here needed it.
- **CPM:** `Aspire.Hosting.AppHost` must not appear in `Directory.Packages.props` (NU1009), because the AppHost SDK references it implicitly.

## Not tested

- The Aspire dashboard and `aspire agent` (MCP) with a live app. The testing builder runs without the dashboard. OTLP export itself was verified separately against a local receiver, from the AOT exe (`../react/RESULTS.md`).
- The desktop host as an Aspire resource; only loopback was run.
- The AOT exe as a resource (`AddExecutable` on the published exe): Aspire ran the Debug build through the project.
- Linux CI, and Windows CI runners (which cannot run Linux containers).
- Persistent containers between test runs (`WithLifetime(ContainerLifetime.Persistent)`), which would remove most of the 20 s.
- Several tests sharing one AppHost, as a fixture.
