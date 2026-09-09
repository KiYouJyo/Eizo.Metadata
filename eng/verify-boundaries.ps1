$ErrorActionPreference = 'Stop'

$projectPath = Join-Path $PSScriptRoot '..\src\Eizo.Metadata.Recognition\Eizo.Metadata.Recognition.csproj'
[xml]$project = Get-Content -Raw $projectPath

$packageReferences = @($project.Project.ItemGroup.PackageReference | Where-Object { $_ })
$projectReferences = @($project.Project.ItemGroup.ProjectReference | Where-Object { $_ })

if ($packageReferences.Count -ne 0) {
    throw 'Eizo.Metadata.Recognition must not have runtime NuGet PackageReference dependencies during the Recognition foundation stages.'
}

if ($projectReferences.Count -ne 0) {
    throw 'Eizo.Metadata.Recognition must not depend on other repository projects.'
}

$targetFramework = [string]$project.Project.PropertyGroup.TargetFramework
if ($targetFramework -ne 'net10.0') {
    throw "Unexpected target framework: $targetFramework"
}

Write-Host 'Recognition dependency boundary verified.'
