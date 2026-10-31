param([string]$GameRoot = $env:KOIKATU_GAME_ROOT, [switch]$Deploy, [string]$KkapiPath = '')
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($GameRoot)) { throw 'Specify -GameRoot or set KOIKATU_GAME_ROOT to your Koikatu directory' }
$GameRoot = (Resolve-Path -LiteralPath $GameRoot).ProviderPath
$projectRoot = $PSScriptRoot
$outputDirectory = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v3.5\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 3.5 C# compiler not found' }
$managed = Join-Path $GameRoot 'Koikatu_Data\Managed'
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) {
    $managed = Join-Path $GameRoot 'Koikatsu Party_Data\Managed'
}
if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw 'Game Managed directory not found' }
$core = Join-Path $GameRoot 'BepInEx\core'
if ([string]::IsNullOrWhiteSpace($KkapiPath)) {
    $plugins = Join-Path $GameRoot 'BepInEx\plugins'
    $candidates = @(if (Test-Path -LiteralPath $plugins) { Get-ChildItem -LiteralPath $plugins -Filter 'KKAPI.dll' -File -Recurse })
    if ($candidates.Count -ne 1) { throw 'Install one official KKAPI 1.48.1 or newer under BepInEx/plugins, or specify -KkapiPath' }
    $KkapiPath = $candidates[0].FullName
}
$KkapiPath = (Resolve-Path -LiteralPath $KkapiPath).ProviderPath
$kkapiIdentity = [Reflection.AssemblyName]::GetAssemblyName($KkapiPath)
if ($kkapiIdentity.Name -ne 'KKAPI' -or $kkapiIdentity.Version -lt [Version]'1.48.1.0') { throw 'Requires the official Koikatu KKAPI.dll version 1.48.1 or newer' }
$references = @('mscorlib.dll','System.dll','System.Core.dll','UnityEngine.dll','UnityEngine.UI.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$references += @('BepInEx.dll','0Harmony.dll') | ForEach-Object { '/reference:' + (Join-Path $core $_) }
$references += '/reference:' + $KkapiPath
$output = Join-Path $outputDirectory 'KK_StoryNPCBridge.dll'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -File | ForEach-Object { $_.FullName })
& $compiler /nologo /noconfig /utf8output /target:library /optimize+ /nostdlib+ /warn:4 /warnaserror+ ('/out:' + $output) @references @sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
if ($Deploy) {
    if (Get-Process -Name 'Koikatu','Koikatsu Party' -ErrorAction SilentlyContinue) { throw 'Close Koikatu and Koikatsu Party before deploying a new plugin build' }
    $destination = Join-Path $GameRoot 'BepInEx\plugins'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $output -Destination (Join-Path $destination 'KK_StoryNPCBridge.dll') -Force
}
Get-Item -LiteralPath $output | Select-Object FullName,Length,LastWriteTime
