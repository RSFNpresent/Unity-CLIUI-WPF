[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'unity-cli-ui.csproj'
$profiles = @(
    'win-x64-framework-dependent',
    'win-x64-self-contained'
)

Push-Location $repoRoot
try {
    $sdkVersion = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -match '-') {
        throw "A stable .NET SDK is required. Detected '$sdkVersion'."
    }

    $version = (& dotnet msbuild $projectPath -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') {
        throw "The project version '$version' is invalid."
    }

    $artifactDirectory = Join-Path $repoRoot "artifacts\v$version"
    New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($profile in $profiles) {
        $publishDirectory = Join-Path $repoRoot "bin\Release\net10.0-windows\publish\$profile"
        if (Test-Path -LiteralPath $publishDirectory) {
            Remove-Item -LiteralPath $publishDirectory -Recurse -Force
        }

        & dotnet publish $projectPath -p:PublishProfile=$profile --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Publish failed for '$profile'."
        }

        $publishedFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File)
        if ($publishedFiles.Count -ne 1 -or $publishedFiles[0].Name -ne 'Unity-CLIUI.exe') {
            throw "Publish '$profile' must contain only Unity-CLIUI.exe."
        }

        $zipPath = Join-Path $artifactDirectory "Unity-CLIUI-v$version-$profile.zip"
        Compress-Archive -LiteralPath $publishedFiles[0].FullName -DestinationPath $zipPath -Force

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            $entryNames = @($archive.Entries | ForEach-Object FullName)
            if ($entryNames.Count -ne 1 -or $entryNames[0] -ne 'Unity-CLIUI.exe') {
                throw "Package '$zipPath' must contain only Unity-CLIUI.exe."
            }
        }
        finally {
            $archive.Dispose()
        }

        $packages.Add([pscustomobject]@{
            Profile = $profile
            Package = $zipPath
            Bytes = (Get-Item -LiteralPath $zipPath).Length
        })
    }

    $packages | Format-Table -AutoSize
}
finally {
    Pop-Location
}
