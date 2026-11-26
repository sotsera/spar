#!/usr/bin/env bash
# The evaluation's preparation (eng/eval/README.md), in the agent image with network: Spar packed from the repository
# (mounted read-only at /repo) into a feed; the two apps the tasks start from, the Notebook from the sample and Pantry
# from the packed template, each with its page's packages installed; and every NuGet package the tasks and their
# reference solutions need, in /eval/prep/nuget, which the runs read as a fallback folder without network.
set -euo pipefail

out=/eval/prep
version_suffix=${EVAL_VERSION_SUFFIX:?}
version="0.1.0-${version_suffix}"
export NUGET_PACKAGES=$out/nuget
export npm_config_cache=/tmp/npm-cache
export DOTNET_CLI_TELEMETRY_OPTOUT=1

rm -rf "$out/feed" "$out/apps" "$out/warm"
mkdir -p "$out/feed" "$out/apps" "$out/warm" "$NUGET_PACKAGES"

# A copy of the repository without its build outputs, so the read-only mount is never written.
echo "== copying the repository"
mkdir -p /tmp/repo
# tar's exit code 1 is "a file changed while it was read" (someone editing the working tree): the copy is still whole.
(cd /repo && tar --exclude=./.git --exclude='*/bin' --exclude='*/obj' --exclude='*/node_modules' --exclude=./artifacts \
    --exclude=./spikes --warning=no-file-changed -cf - . || [ $? -eq 1 ]) | (cd /tmp/repo && tar -xf -)
cd /tmp/repo

echo "== the TypeScript SDK"
(cd src/Spar.Client && npm ci --ignore-scripts && npm run build)

echo "== packing Spar $version"
for project in Spar Spar.Windows Spar.Linux Spar.Loopback Spar.OpenTelemetry Spar.Testing Spar.Testing.Browser Spar.Sdk Spar.Templates Spar.Tool; do
    dotnet pack "src/$project/$project.csproj" -c Release "-p:VersionSuffix=$version_suffix" -o "$out/feed" -nologo -v:q
done

# The apps' nuget.config while preparing: the feed for Spar's packages, nuget.org for the rest.
online_config() {
    cat > "$1/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="spar" value="$out/feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="spar"><package pattern="Spar" /><package pattern="Spar.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
XML
}

echo "== Pantry, from the template"
hive=/tmp/template-hive
dotnet new install "$out/feed/Spar.Templates.$version.nupkg" --debug:custom-hive "$hive"
dotnet new spar-app -n Pantry -o "$out/apps/Pantry" --debug:custom-hive "$hive"
online_config "$out/apps/Pantry"

echo "== the Notebook, from the sample"
notebook=$out/apps/Notebook
mkdir -p "$notebook"
cp -r samples/Notebook samples/Notebook.Tests samples/Notebook.EndToEndTests "$notebook/"
cp "$out/apps/Pantry/global.json" "$notebook/global.json"
mkdir -p "$notebook/.config" && cp "$out/apps/Pantry/.config/dotnet-tools.json" "$notebook/.config/"
cp "$out/apps/Pantry/.gitignore" "$notebook/.gitignore"
online_config "$notebook"
# What the repository's Directory.Build.props gives the sample, and a template app writes in its own projects.
cat > "$notebook/Directory.Build.props" <<'XML'
<Project>
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <MSBuildTreatWarningsAsErrors>true</MSBuildTreatWarningsAsErrors>
  </PropertyGroup>
</Project>
XML
# Left out: the browser journeys (Chromium is not in the image) and the Windows Sandbox tests (Windows only).
rm -rf "$notebook/Notebook.EndToEndTests/Sandbox" "$notebook/Notebook.EndToEndTests/JourneyTests.cs" "$notebook/Notebook.EndToEndTests/NotebookPage.cs"
perl -0pi -e 's/\n  <!-- The Browser surface.*?\n  <ItemGroup Condition="\x27\$\(SparTestLane\)\x27 == \x27Aot\x27">.*?<\/ItemGroup>\n//s' "$notebook/Notebook.EndToEndTests/Notebook.EndToEndTests.csproj"
grep -q 'Include="Microsoft.Playwright"\|<SparBrowserTests>' "$notebook/Notebook.EndToEndTests/Notebook.EndToEndTests.csproj" \
    && { echo "The Notebook's end-to-end project still names the browser tests." >&2; exit 1; }
(cd "$notebook" && dotnet new sln --format slnx -n Notebook && dotnet sln add Notebook/Notebook.csproj Notebook.Tests/Notebook.Tests.csproj Notebook.EndToEndTests/Notebook.EndToEndTests.csproj)

for app in Pantry Notebook; do
    echo "== $app: restore, tools, the page's packages and a first build"
    (cd "$out/apps/$app" && dotnet tool restore && dotnet build -c Debug -nologo -v:q)
done

# Each task's reference solution, once, in a scratch copy: it restores every package the task can need (the layer,
# NativeAOT's compiler for linux-x64), which the offline runs then find in the fallback folder.
export EVAL_SPEC=/repo/eng/eval EVAL_LIB=/repo/eng/eval/lib.sh
for task in /repo/eng/eval/tasks/*/; do
    name=$(basename "$task")
    case "$name" in notebook-*) app=Notebook ;; *) app=Pantry ;; esac
    echo "== warming $name"
    rm -rf "$out/warm/$app" && cp -a "$out/apps/$app" "$out/warm/$app"
    (
        cd "$out/warm/$app"
        export EVAL_TASK=$task
        if [ -f "$task/seed.sh" ]; then bash "$task/seed.sh"; fi
        bash "$task/reference.sh"
        dotnet build -c Debug -nologo -v:q
    )
done
rm -rf "$out/warm"

# The apps as an agent finds them: no build outputs (their page packages stay), and the offline nuget.config.
for app in Pantry Notebook; do
    find "$out/apps/$app" -name node_modules -prune -o -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} +
    rm -f "$out/apps/$app/nuget.config"
done
echo "$version" > "$out/version"
echo "== prepared Spar $version"

# What the profiles add (in-container.sh): the plugin's skills and the template's agent notes, as of this preparation.
rm -rf "$out/skills" && cp -r /repo/plugins/spar/skills "$out/skills"
cp /repo/src/Spar.Templates/content/spar-app/AGENTS.md "$out/agents-template.md"
