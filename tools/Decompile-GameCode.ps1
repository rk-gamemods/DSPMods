[CmdletBinding()]
param([string]$GameDir)
$ErrorActionPreference = 'Stop'
$captureArgs = @('-3', (Join-Path $PSScriptRoot 'game_snapshot.py'), 'capture')
if ($GameDir) { $captureArgs += @('--game', $GameDir) }
& py @captureArgs
if ($LASTEXITCODE -ne 0) { throw "DSP capture failed (exit $LASTEXITCODE)." }
