param([Parameter(Mandatory)][string]$GameDir, [string]$CandidateDll = 'dist/Sts2Demo/Sts2Demo.dll')
$ErrorActionPreference = 'Stop'
$repoDir = Split-Path -Parent $PSScriptRoot
function Wait-LabResult([string]$Name, [string]$Marker, [int]$Minutes) {
    $deadline = [DateTime]::UtcNow.AddMinutes($Minutes)
    $logPath = Join-Path $repoDir "artifacts/validation-1.4.0/$Name"
    do {
        Start-Sleep -Seconds 2
        $logText = Get-Content -LiteralPath $logPath -Raw
        if ($logText.Contains('[ERROR]') -or $logText.Contains('FAIL')) { throw "Lab error in $Name; preserving evidence." }
    } while (-not $logText.Contains($Marker) -and [DateTime]::UtcNow -lt $deadline)
    if (-not $logText.Contains($Marker)) { throw "Timeout in $Name; preserving evidence." }
}
& "$PSScriptRoot/Run-Lab.ps1" -GameDir $GameDir -CandidateDll $CandidateDll
Wait-LabResult 'lab-current.log' '[DemoLab] 1.4.0 LAB FINISHED.' 15
Write-Output 'FULL CANDIDATE PASS'
& "$PSScriptRoot/Run-Lab.ps1" -GameDir $GameDir -CandidateDll $CandidateDll -StressOnly
Wait-LabResult 'lab-stress.log' '[DemoLab] STRESS FINISHED.' 8
Write-Output 'STRESS CANDIDATE PASS'
& "$PSScriptRoot/Run-Lab.ps1" -GameDir $GameDir -StopOnly
& "$PSScriptRoot/Run-StartupAudit.ps1" -Runs 8
