# Step 3 of the haptic authoring tool for Boxing: applies Assets\Boxing\Haptics\boxing-cues.json (as saved by
# Hapbeat Studio) to BoxingEventMap and the scene: each cue's clip (Assets\Boxing\Haptics\stream-clips\<clip>.wav),
# route gain and clip intensity into left_<cue> / right_<cue> (wrist cues) or <cue> (hit_body, neck), and its
# sound (Assets\Boxing\Haptics\sounds\<Name>.wav) and volume into BoxingFeedback (build_haptic_import.py checks
# and flattens the table, BoxingHapticImport writes it). Close the Unity Editor on this project first.
# Rebuild and reinstall the APK afterwards. -EngineRoot is passed by the launcher (Unreal) and ignored.
param([string]$Unity=$(if($env:BOXING_UNITY){$env:BOXING_UNITY}else{'M:\GameEngine\Unity\Editor\6000.3.12f1\Editor\Unity.exe'}),
    [string]$EngineRoot)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot
$log=Join-Path $projectRoot 'Saved\Logs\ImportHapticKit.log'
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
python "$PSScriptRoot\build_haptic_import.py"
if($LASTEXITCODE -ne 0) { throw 'Cue table check failed (see above)' }
& $Unity -batchmode -quit -nographics -projectPath $projectRoot -buildTarget Win64 -executeMethod Hapbeat.Boxing.Editor.BoxingHapticImport.Run -logFile $log | Out-Null
if(-not (Select-String -Path $log -Pattern 'HAPTIC_KIT_IMPORTED' -Quiet)) { throw "Import failed: exit $LASTEXITCODE (see $log)" }
Select-String -Path $log -Pattern 'HAPTIC_KIT_IMPORT' | ForEach-Object { $_.Line }
