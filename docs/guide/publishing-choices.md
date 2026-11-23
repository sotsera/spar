---
title: Publishing choices
description: Publish a Spar app on CoreCLR, accept a library's AOT warnings, turn culture data on, and use Microsoft.Data.SqlClient.
---

# Publishing choices

`dotnet publish -c Release` makes one NativeAOT executable for the OS you run it on: no .NET on the user's machine, a fast start, and a small file. A Spar app gets that, and three more settings, from Spar.Sdk unless its own project says otherwise:

| Setting | Spar's default | What an app may choose |
|---|---|---|
| The runtime | NativeAOT (`PublishAot` true) | CoreCLR: framework-dependent, or a self-contained single file |
| Trim and AOT warnings | errors, in an app made from the template (`TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors`) | accept named ones, which stay warnings on every publish |
| Culture data | none (`InvariantGlobalization` true) | `InvariantGlobalization` false |

Each choice is a few lines in the app's own project file (`Notes/Notes.csproj` for an app named Notes), and only that app's. Spar.Sdk never sets them for an app, and a choice one library needs belongs in the project of the app that uses the library. The blocks below go inside `<Project>`, after the template's own groups; each is complete, so you can add one or several. They are the blocks Spar's own tests add to a new app and publish.

## 1. The default: NativeAOT

The template's app publishes with [NativeAOT](https://learn.microsoft.com/dotnet/core/deploying/native-aot/): the compiler turns the app, Spar and every library into one native executable and removes the code nothing calls. The app needs no .NET runtime, starts quickly, and weighs about 4.7 MB. Publishing needs the native build tools (the C++ build tools of Visual Studio on Windows, `clang` and the zlib development files on Linux), and it does not cross-compile: publish each OS's executable on that OS.

NativeAOT knows the whole program when it compiles, so it reports code that may not work without the runtime's reflection and dynamic code: **trim warnings** (`IL2xxx`) and **AOT warnings** (`IL3xxx`). In an app from the template every warning fails the build, so a publish that reports one fails. Spar's own code reports none. A library written before NativeAOT often does; sections 3 and 5 show what to do then.

## 2. Publish on CoreCLR

An app can publish on CoreCLR, .NET's ordinary runtime with its just-in-time compiler, instead. Choose it when a library the app needs does not work under NativeAOT, or when the machine you publish on has no native build tools. The app runs the same: the same window, page and bridge, the same Production build. One difference: NativeAOT removes the code of Spar's test and development features from the executable, while on CoreCLR it stays in Spar's assemblies, switched off by the publication's runtime configuration (`Notes.runtimeconfig.json`, inside the file for a single file). It starts a little later (45–65 ms in the measurements below, where most of the start is the web engine's), its first calls into new backend code are slower while the runtime compiles them, and it uses more memory.

Spar sets the app's runtime identifier to the OS you build on (`win-x64` on Windows x64, `linux-x64` on Linux x64), so a CoreCLR publication is for that OS and architecture, as a NativeAOT one is ([runtime identifiers](https://learn.microsoft.com/dotnet/core/rid-catalog)).

### Framework-dependent

The publication needs .NET 11 installed on the user's machine, and is the smallest on disk ([framework-dependent deployment](https://learn.microsoft.com/dotnet/core/deploying/#publish-as-framework-dependent)):

<!-- project-snippet: coreclr-framework-dependent -->
```xml
<PropertyGroup>
  <!-- CoreCLR, using the .NET runtime installed on the user's machine. -->
  <PublishAot>false</PublishAot>
</PropertyGroup>
```

`dotnet publish -c Release` then writes a folder, not a single file: the launcher `Notes.exe` (`Notes` on Linux), the app's `Notes.dll`, Spar's assemblies, `Notes.runtimeconfig.json` and `Notes.deps.json`, and on Windows `WebView2Loader.dll`, which finds the WebView2 Runtime (under NativeAOT it is linked into the executable). Ship the whole folder. The user's machine needs the .NET 11 runtime (`Microsoft.NETCore.App`; the ASP.NET Core runtime is not needed), installed where the launcher looks for it, or `DOTNET_ROOT` naming it ([install .NET on Windows](https://learn.microsoft.com/dotnet/core/install/windows), [on Linux](https://learn.microsoft.com/dotnet/core/install/linux)).

### Self-contained single file

The runtime goes into the executable, so the user's machine needs no .NET, at the cost of size ([single-file deployment](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview)):

<!-- project-snippet: coreclr-single-file -->
```xml
<PropertyGroup>
  <!-- CoreCLR, with the runtime and every library inside one executable. -->
  <PublishAot>false</PublishAot>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>

  <!-- The native libraries (on Windows the WebView2 loader) inside it too, extracted when the app starts. -->
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
</PropertyGroup>
```

The publish folder then holds the executable alone, besides its debug symbols (`Notes.pdb`), which you do not ship.

- **On Windows** the WebView2 loader is a native library. With `IncludeNativeLibrariesForSelfExtract`, the executable extracts it at its first start into `%TEMP%\.net\Notes\<hash>\` (or under `DOTNET_BUNDLE_EXTRACT_BASE_DIR` when that is set) and loads it from there. Without that line, `WebView2Loader.dll` stays beside the executable and ships with it, and nothing is extracted.
- **On Linux** Spar loads GTK 4 and WebKitGTK 6.0 from the system, as a NativeAOT app does, so there is nothing to extract.
- `<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>` makes the file about half the size and its start slower ([compression](https://learn.microsoft.com/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps)). `spar doctor --app` cannot read the build identity of a compressed bundle.

### What each choice weighs

Measured on the template's app: the publication's size without its debug symbols, and the time from the process's start to the page's first ready signal (`ui-ready` in the app's log), the median of five starts. Windows was a Windows 11 x64 desktop, Linux Ubuntu 24.04 in WSL on the same machine, drawing on an X server without a GPU; your numbers will differ, the proportions less so.

| Publication | Windows (win-x64) | Linux (linux-x64) | Needs on the user's machine |
|---|---|---|---|
| NativeAOT (default) | 4.7 MB, 1 file; 378 ms | 4.7 MB, 1 file; 201 ms | the web engine |
| CoreCLR framework-dependent | 1.8 MB, 10 files; 423 ms | 1.6 MB, 9 files; 263 ms | the web engine and .NET 11 |
| CoreCLR self-contained single file | 81.2 MB, 1 file; 441 ms | 79.0 MB, 1 file; 265 ms | the web engine |

On both OSes the web engine is the same as for NativeAOT: the WebView2 Runtime on Windows, GTK 4 and WebKitGTK 6.0 on Linux (getting-started guide, section 1). A CoreCLR app on Linux also needs the libraries .NET itself needs there ([.NET on Linux](https://learn.microsoft.com/dotnet/core/install/linux)); `spar doctor` checks ICU and OpenSSL.

Publish each shape into an empty folder (`-o`, or after deleting `bin/Production/`): a publish folder keeps the files of an earlier shape.

## 3. Accept a library's AOT warnings

When a library reports trim or AOT warnings, the app can still publish with NativeAOT if you accept them. You then own the risk: the code the warnings point at may fail at run time, and only when it runs, so test the features the app uses against the published executable (the template's black-box test runs it).

By default the publish reports a library's warnings once per assembly (the SDK's `TrimmerSingleWarn` is true): `IL2104` "Assembly '…' produced trim warnings" and `IL3053` "Assembly '…' produced AOT analysis warnings", with the assembly's path. Accept those IDs so they stay warnings, never errors:

<!-- project-snippet: accept-aot-warnings -->
```xml
<PropertyGroup>
  <!-- A library's trim and AOT warnings stay warnings: every publish shows them, none fails. -->
  <WarningsNotAsErrors>$(WarningsNotAsErrors);IL2104;IL3053</WarningsNotAsErrors>
</PropertyGroup>
```

`WarningsNotAsErrors` reaches both places that would turn them into errors: the NativeAOT compiler (`TreatWarningsAsErrors` makes it treat warnings as errors, and this list exempts these IDs) and MSBuild (`MSBuildTreatWarningsAsErrors`, whose own list, `MSBuildWarningsNotAsErrors`, is this one unless the project sets it; if yours does, add the IDs there too). The publish succeeds, and its output still lists every accepted warning with the assembly it names ([trimming options, "Suppress warnings"](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options#suppress-warnings)).

What this accepts:

- **Whole assemblies.** One `IL2104` stands for every trim warning inside its assembly, so accepting it accepts them all, including those of a later version of the library.
- **Every assembly.** The list holds IDs, not assemblies: a library you add later that reports `IL2104` or `IL3053` publishes too. Read the warnings each publish prints; they name the assemblies.
- **Not your own code.** The app's own code reports its warnings member by member, with other IDs (`IL2026`, `IL3050` and the others), from the compiler as you build. Those still fail the build.

Do not use `NoWarn` for these IDs: it hides them, so a new assembly's warnings would pass unseen.

To see what one `IL2104` stands for, publish once with the warnings of every assembly shown member by member ([show detailed warnings](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options#show-detailed-warnings)):

```sh
dotnet publish Notes -c Release -p:TrimmerSingleWarn=false
```

That publish fails: each member's warning has its own ID, which the block above does not accept. It is for reading; the compiler lists every warning before it stops. Do not add those IDs to `WarningsNotAsErrors`: they are the same IDs the compiler reports for your own code, which would then stop failing the build. For the warnings themselves, see [introduction to trim warnings](https://learn.microsoft.com/dotnet/core/deploying/trimming/fixing-warnings) and [introduction to AOT warnings](https://learn.microsoft.com/dotnet/core/deploying/native-aot/fixing-warnings).

## 4. Culture data: InvariantGlobalization

A Spar app is invariant by default: every culture behaves as the invariant culture, so the backend formats, parses, compares and changes case the same way on every machine, and needs no culture data ([invariant mode](https://learn.microsoft.com/dotnet/core/runtime-config/globalization#invariant-mode)). The page is not affected: the web engine has its own culture data.

Turn culture data on when backend code must follow cultures (a date formatted for the user's locale, culture-aware sorting), or when a library refuses invariant mode, as Microsoft.Data.SqlClient does (section 5):

<!-- project-snippet: globalization -->
```xml
<PropertyGroup>
  <!-- The backend uses the system's culture data (ICU). -->
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

The cost: the template's NativeAOT executable grew by 218,112 B on Windows (4,745,216 to 4,963,328 B) and by 185,424 B on Linux (4,679,880 to 4,865,304 B). On Windows .NET uses the ICU that comes with Windows, with no extra files. On Linux the user's machine needs the ICU libraries, and an app without them does not start; `spar doctor` checks for them (`linux-icu`) and names the package to install ([ICU on Linux](https://learn.microsoft.com/dotnet/core/extensions/globalization-icu)).

## 5. Example: an app that uses Microsoft.Data.SqlClient

[Microsoft.Data.SqlClient](https://learn.microsoft.com/sql/connect/ado-net/introduction-microsoft-data-sqlclient-namespace), SQL Server's .NET client, is not written for NativeAOT, refuses invariant mode, and on Windows loads a native networking library by default. An app that uses it with NativeAOT puts three of the blocks above and two of its own in its project.

The package:

<!-- project-snippet: sqlclient-package -->
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Data.SqlClient" Version="7.1.2" />
</ItemGroup>
```

Its warnings: the block of section 3. SqlClient 7.1.2 reports `IL2104` for `Microsoft.Data.SqlClient` and `System.Configuration.ConfigurationManager`, and `IL3053` for `Microsoft.Data.SqlClient`; without the block the publish fails with them as errors. With `-p:TrimmerSingleWarn=false` a new app that only opens a connection lists 31 warnings, in the authentication providers and retry logic it loads from configuration, user-defined types, its diagnostic listener and the `vector` type.

Culture data: the block of section 4. In invariant mode `SqlConnection.Open` throws `NotSupportedException` ("Globalization Invariant Mode is not supported") before it touches the network.

One file on Windows: by default SqlClient uses a native library, `Microsoft.Data.SqlClient.SNI.dll`, which the publish puts beside the executable, and the executable alone fails at the first `Open` with `DllNotFoundException`. Its managed networking needs no native library, so the executable is the whole publication:

<!-- project-snippet: sqlclient-single-file -->
```xml
<ItemGroup>
  <!-- SqlClient's managed networking on Windows, so the publication needs no native SNI library. -->
  <RuntimeHostConfigurationOption Include="Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows" Value="true" Trim="true" />

  <!-- The native SNI package SqlClient brings, left out of the publication. Keep its version SqlClient's. -->
  <PackageReference Include="Microsoft.Data.SqlClient.SNI.runtime" Version="7.1.2" ExcludeAssets="all" PrivateAssets="all" />
</ItemGroup>
```

`Trim="true"` lets the NativeAOT compiler remove the native networking code as well as switch it off. On Linux SqlClient always uses managed networking and the SNI package has nothing for it, so the block changes nothing there. The publish folder is then the executable alone on both OSes, besides its debug symbols.

What managed networking changes, from SqlClient's documentation ([AppContext switches](https://learn.microsoft.com/sql/connect/ado-net/appcontext-switches#enable-managed-networking-on-windows)) and from Spar's checks:

- SqlClient's documentation describes the switch as meant for testing and debugging, and says managed networking does not support non-domain Windows authentication.
- **Not tested by Spar:** Windows integrated authentication, named pipes and shared memory with managed networking on Windows. Spar's tests check only that a published executable reaches the network: an `Open` against a port where nothing listens fails with `SqlException`, not with a missing library. If the app needs one of these, test it, or keep the native library beside the executable (leave this block out).
- **On Linux** (managed networking always): TCP only, so named pipes and shared memory do not exist there, and integrated authentication is Kerberos only, which needs `libgssapi_krb5.so.2`.

Everything else about SqlClient is the same as in any .NET app: its connection strings, encryption (`Encrypt` is mandatory by default, so a server with a self-signed certificate needs `TrustServerCertificate=true` or a trusted certificate) and its own documentation.
