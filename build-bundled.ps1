param(
    [string]$Version = "2.6.0",
    [string]$Output = "$PSScriptRoot\artifacts\bundled"
)

$ErrorActionPreference = "Stop"

$Project = Join-Path $PSScriptRoot "src\Confluent.Kafka.Bundled\Confluent.Kafka.Bundled.csproj"
$BuildOutput = Join-Path $PSScriptRoot "src\Confluent.Kafka.Bundled\bin\Release\net462"

Write-Host "Restoring bundled Confluent.Kafka..."
dotnet restore $Project
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

Write-Host "Building single bundled Confluent.Kafka.dll..."
dotnet build $Project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

New-Item -ItemType Directory -Force -Path $Output | Out-Null

Write-Host "Packing Confluent.Kafka $Version..."
dotnet pack $Project -c Release --no-build -p:PackageVersion=$Version -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed" }

$Dll = Join-Path $BuildOutput "Confluent.Kafka.dll"
$Nuget = Join-Path $Output ("Confluent.Kafka.{0}.nupkg" -f $Version)

Write-Host ""
Write-Host "DONE"
Write-Host "Bundled DLL: $Dll"
Write-Host "NuGet:      $Nuget"
