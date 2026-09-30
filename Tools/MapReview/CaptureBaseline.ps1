param([Parameter(Mandatory=$true)][string]$SourceBlend, [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path '.').Path
$reportDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$gitState = git status --porcelain=v1
$paths = @('Assets/Scenes/Map.unity', 'Assets/Scripts/Map/RunSession.cs', 'Assets/Scripts/Map/BattleRunBridge.cs', 'Assets/Editor/MapEnvironmentIntegration.cs', 'ProjectSettings/EditorBuildSettings.asset', $SourceBlend)
$hashes = foreach ($entry in $paths) {
    $resolved = (Resolve-Path -LiteralPath $entry).Path
    $item = Get-Item -LiteralPath $resolved
    [pscustomobject]@{path=$resolved; length=$item.Length; sha256=(Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash}
}
$report = [pscustomobject]@{capturedUtc=[DateTime]::UtcNow.ToString('o'); project=$projectRoot; workingTree=@($gitState); protectedFiles=@($hashes)}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportDirectory 'protected-baseline.json') -Encoding UTF8
$report.protectedFiles | Format-Table path,length
