# Step 1 of the haptic authoring tool (repos-tools/hapbeat-haptic-authoring in the Hapbeat SDK
# workspace; its launcher calls this) for Boxing. A PC replay of every cue, three takes each: scripted
# punches by the player's gloves and strikes by the opponent, rendered at a fixed 30 fps from the player's
# eye (no sound, nothing sent to Hapbeat devices; BoxingHapticRecord, which fails if a take fires anything but
# its cue), then haptic_session.py has the tool cut the clips per cue into Saved\HapticViewer (open Hapbeat
# Studio's Scene tab and pick this project folder).
# Needs ffmpeg with libx264 (on PATH, or -Ffmpeg <path>) and Python 3.8+ on PATH. Close the Unity Editor on
# this project first. -EngineRoot is passed by the launcher (Unreal) and ignored.
param([string]$Unity=$(if($env:BOXING_UNITY){$env:BOXING_UNITY}else{'M:\GameEngine\Unity\Editor\6000.3.12f1\Editor\Unity.exe'}),
    [string]$Ffmpeg='ffmpeg', [switch]$KeepFrames, [string]$EngineRoot,
    [string]$Tool=$(Join-Path (Split-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot)))) 'repos-tools\hapbeat-haptic-authoring'))
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$log=Join-Path $projectRoot 'Saved\Logs\HapticRecord.log'
if(-not (Test-Path "$Tool\build_viewer.py")) { throw "hapbeat-haptic-authoring not found at $Tool (pass -Tool)" }
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
# With graphics (the frames are rendered); -batchmode keeps it headless and silent.
& $Unity -batchmode -quit -projectPath $projectRoot -buildTarget Win64 -executeMethod Hapbeat.Boxing.Editor.BoxingHapticRecord.Run -logFile $log | Out-Null
if(!(Select-String -Path $log -Pattern 'HAPTIC_RECORD_COMPLETE' -Quiet)) {
    Select-String -Path $log -Pattern 'HAPTIC_RECORD_FAILED' | Select-Object -First 1 | ForEach-Object { $_.Line }
    throw "Replay did not complete (see $log)"
}
Select-String -Path $log -Pattern 'HAPTIC_RECORD_CUE|HAPTIC_RECORD_COMPLETE' | ForEach-Object { $_.Line }
$py=@("$PSScriptRoot\haptic_session.py",'--tool',$Tool,'--ffmpeg',$Ffmpeg)
if($KeepFrames) { $py+='--keep-frames' }
python @py
if($LASTEXITCODE -ne 0) { throw 'Viewer build failed' }
