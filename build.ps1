param([string]$GameRoot = $env:KOIKATU_GAME_ROOT, [switch]$Deploy)
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
$references = @('mscorlib.dll','System.dll','System.Core.dll','UnityEngine.dll','UnityEngine.UI.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll') | ForEach-Object { '/reference:' + (Join-Path $managed $_) }
$references += @('BepInEx.dll','0Harmony.dll') | ForEach-Object { '/reference:' + (Join-Path $core $_) }
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
