using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    /// <summary>
    /// Step 3 of the haptic authoring tool for Boxing: writes Saved/HapticImport.json (the cue table flattened
    /// by Scripts/build_haptic_import.py) into BoxingEventMap and the scene's BoxingFeedback: per cue its stream
    /// clip (Assets/Boxing/Haptics/stream-clips/&lt;clip&gt;.wav), route gain and clip intensity (cached manifest
    /// intensity, so the effective gain = gain x intensity as with a Kit) into left_&lt;cue&gt; and right_&lt;cue&gt;
    /// (wrist cues) or &lt;cue&gt; (hit_body, neck), and its sound (Assets/Boxing/Haptics/sounds/&lt;Name&gt;.wav,
    /// none when empty) and volume into BoxingFeedback.cueSounds / cueSoundVolumes. The clips and sounds get the
    /// import settings of BoxingContent.ImportAudio (decompressed PCM; the SDK reads the clips' samples).
    /// Only what differs is written, so importing the checked-in table changes nothing.
    ///
    /// Run: Unity -batchmode -quit -nographics -projectPath &lt;project&gt; -executeMethod Hapbeat.Boxing.Editor.BoxingHapticImport.Run
    /// </summary>
    public static class BoxingHapticImport
    {
        public const string MapPath = "Assets/Boxing/Haptics/BoxingEventMap.asset";

        [Serializable] private sealed class Plan { public Item[] items; }
        [Serializable] private sealed class Item { public string cue; public string clip; public float gain; public float intensity; public string sound; public float volume; }

        public static void Run()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Path.Combine(root, "Saved", "HapticImport.json")));
            AssetDatabase.Refresh();
            var map = AssetDatabase.LoadAssetAtPath<Hapbeat.HapbeatEventMap>(MapPath);
            if (map == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: " + MapPath + " missing");
            var scene = EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var feedback = UnityEngine.Object.FindFirstObjectByType<BoxingGame>().feedback;
            if (plan.items.Length != BoxingFeedback.Cues.Length) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: the plan has " + plan.items.Length + " cues");
            int entries = 0, mapChanges = 0, sceneChanges = 0;
            foreach (var item in plan.items)
            {
                int cue = Array.IndexOf(BoxingFeedback.Cues, item.cue);
                if (cue < 0) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: unknown cue " + item.cue);
                var clip = BoxingContent.ImportAudio(BoxingContent.StreamClips + item.clip + ".wav");
                if (clip == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: clip " + item.clip + " missing");
                var names = cue < BoxingFeedback.WristCues ? new[] { "left_" + item.cue, "right_" + item.cue } : new[] { item.cue };
                foreach (var name in names)
                {
                    var entry = map.entries.Find(e => e != null && e.eventName == name);
                    if (entry == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: " + MapPath + " has no entry " + name);
                    if (entry.streamClip != clip || entry.loop || !Mathf.Approximately(entry.gain, item.gain)
                        || !Mathf.Approximately(entry.CachedManifestIntensity, item.intensity) || entry.manifestOverride != null)
                    {
                        entry.streamClip = clip; entry.loop = false; entry.gain = item.gain;
                        // The intensity lives in the cue table, not in a Kit manifest.
                        entry.manifestOverride = null; entry.SetCachedManifestIntensity(item.intensity);
                        mapChanges++;
                    }
                    entries++;
                    Debug.Log("HAPTIC_KIT_IMPORT " + name + " <- " + item.clip + " gain=" + item.gain + " intensity=" + item.intensity);
                }
                var sound = string.IsNullOrEmpty(item.sound) ? null : BoxingContent.ImportAudio(BoxingContent.CueSounds + item.sound + ".wav");
                if (!string.IsNullOrEmpty(item.sound) && sound == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: sound " + item.sound + " missing");
                if (feedback.cueSounds[cue] != sound || !Mathf.Approximately(feedback.cueSoundVolumes[cue], item.volume))
                {
                    feedback.cueSounds[cue] = sound; feedback.cueSoundVolumes[cue] = item.volume;
                    sceneChanges++;
                }
                Debug.Log("HAPTIC_KIT_IMPORT sound " + item.cue + " <- " + (sound == null ? "(none)" : item.sound) + " volume=" + item.volume);
            }
            if (mapChanges > 0) { EditorUtility.SetDirty(map); AssetDatabase.SaveAssets(); }
            if (sceneChanges > 0) { EditorUtility.SetDirty(feedback); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            Debug.Log("HAPTIC_KIT_IMPORTED cues=" + plan.items.Length + " entries=" + entries + " changed=" + (mapChanges + sceneChanges));
        }
    }
}
