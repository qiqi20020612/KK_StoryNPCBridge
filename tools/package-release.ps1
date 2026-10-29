param(
    [string]$PluginPath = (Join-Path $PSScriptRoot '../artifacts/build/KK_StoryNPCBridge.dll'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/release'),
    [string]$Tag = ''
)
$ErrorActionPreference = 'Stop'
$PluginPath = (Resolve-Path -LiteralPath $PluginPath).ProviderPath
$assembly = [Reflection.AssemblyName]::GetAssemblyName($PluginPath)
$metadata = [Diagnostics.FileVersionInfo]::GetVersionInfo($PluginPath)
$version = $metadata.ProductVersion
if ($assembly.Name -ne 'KK_StoryNPCBridge' -or $version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Unexpected plugin identity or product version'
}
if ($assembly.Version.ToString() -ne "$version.0" -or $metadata.FileVersion -ne "$version.0") {
    throw 'Assembly, file and product versions must match'
}
if ($Tag -and $Tag -cne "v$version") { throw "Tag $Tag does not match plugin version $version" }
$licensePath = Join-Path $PSScriptRoot '../LICENSE'
if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) { throw 'MIT license file is missing' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath
$dllPath = Join-Path $OutputDirectory 'KK_StoryNPCBridge.dll'
Copy-Item -LiteralPath $PluginPath -Destination $dllPath -Force
$zipPath = Join-Path $OutputDirectory "KK_StoryNPCBridge-$version.zip"
Add-Type -AssemblyName System.IO.Compression
$stream = [IO.File]::Open($zipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entries = @(
        @{ Source = $dllPath; Name = 'BepInEx/plugins/KK_StoryNPCBridge.dll' },
        @{ Source = $licensePath; Name = 'LICENSE' }
    )
    foreach ($item in $entries) {
        $entry = $archive.CreateEntry($item.Name, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $inputStream = [IO.File]::OpenRead($item.Source)
        $outputStream = $entry.Open()
        try { $inputStream.CopyTo($outputStream) }
        finally { $outputStream.Dispose(); $inputStream.Dispose() }
    }
}
finally { $archive.Dispose(); $stream.Dispose() }
$checksums = foreach ($path in @($dllPath, $zipPath)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), ($checksums -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
[PSCustomObject]@{ Version = $version; Directory = $OutputDirectory }
