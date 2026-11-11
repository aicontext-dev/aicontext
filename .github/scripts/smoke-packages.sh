#!/usr/bin/env bash
# Smoke test of the NuGet packages as they will be pushed. AIContext and AIContext.CLI are taken only from the files in
# <nupkg-dir>, never from a published copy of the same version, and the packages folder is private to the run, so a
# cached copy cannot stand in for them. Needs the .NET 9 and .NET 10 SDKs and runtimes.
#
#   smoke-packages.sh <nupkg-dir>
#
# <nupkg-dir> is the output of pack.sh.
set -euo pipefail

dir="$(cd "${1:?usage: smoke-packages.sh <nupkg-dir>}" && pwd)"
cd "$(dirname "$0")/../.."

lib_version="$(awk '$1 == "AIContext" { print $2 }' "$dir/release-packages.txt")"
cli_version="$(awk '$1 == "AIContext.CLI" { print $2 }' "$dir/release-packages.txt")"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export NUGET_PACKAGES="$work/packages"

echo "== The tool from the package, on .NET 10 (the default) and on .NET 9"
tool="$work/tool"
dotnet tool install AIContext.CLI --version "$cli_version" --source "$dir" --tool-path "$tool"
bash .github/scripts/smoke-cli.sh "$tool/aicontext" "$cli_version"

dll9="$(find "$tool/.store" -path '*/tools/net9.0/any/AIContext.CLI.dll' | head -n 1)"
if [ -z "$dll9" ]; then
  echo "::error::The tool package has no net9.0 build."
  exit 1
fi
printf '#!/usr/bin/env bash\nexec dotnet "%s" "$@"\n' "$dll9" > "$work/aicontext-net9"
chmod +x "$work/aicontext-net9"
bash .github/scripts/smoke-cli.sh "$work/aicontext-net9" "$cli_version"

echo "== The library from the package, on .NET 9 and .NET 10"
consumer="$work/consumer"
mkdir -p "$consumer"
cat > "$consumer/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="release-artifacts" value="$dir" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="release-artifacts">
      <package pattern="AIContext" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF
cat > "$consumer/Consumer.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFrameworks>net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="AIContext" Version="$lib_version" />
  </ItemGroup>
</Project>
EOF
cat > "$consumer/Program.cs" <<'EOF'
using System.Runtime.InteropServices;
using AIContext;

var expectedMajor = int.Parse(args[0].Split('.')[0]);
if (Environment.Version.Major != expectedMajor)
{
    Console.Error.WriteLine($"Expected .NET {expectedMajor}, running {Environment.Version}.");
    return 1;
}

var dir = Path.Combine(Path.GetTempPath(), "aicontext-package-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var init = ProjectInitializer.Initialize(dir, "package-smoke", InitProfile.Core, ContextSchema.CanonicalUrl, "0.1.0", force: false);
    if (init.HasErrors)
    {
        Console.Error.WriteLine("init failed: " + string.Join("; ", init.Diagnostics.Select(d => d.Message)));
        return 1;
    }

    var valid = ContextValidator.ValidateDirectory(dir, ContextSchema.CanonicalUrl);
    if (valid.Errors.Count > 0)
    {
        Console.Error.WriteLine("a fresh project is invalid: " + string.Join("; ", valid.Errors));
        return 1;
    }

    // The schema is an embedded resource of the package; it must still reject invalid input.
    File.WriteAllText(Path.Combine(dir, ".aicontext", "context.json"), """{ "specVersion": "0.1.0", "project": {} }""");
    if (ContextValidator.ValidateDirectory(dir, ContextSchema.CanonicalUrl).Errors.Count == 0)
    {
        Console.Error.WriteLine("an invalid context.json was accepted");
        return 1;
    }

    Console.WriteLine($"AIContext library works on {RuntimeInformation.FrameworkDescription}");
    return 0;
}
finally
{
    Directory.Delete(dir, recursive: true);
}
EOF
for tfm in net9.0 net10.0; do
  dotnet run --project "$consumer" --framework "$tfm" -- "${tfm#net}"
done

echo "Package smoke test passed."
