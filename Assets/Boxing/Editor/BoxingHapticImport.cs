using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    /// <summary>
    /// Step 3 of the haptic authoring tool for Boxing: writes Saved/HapticImport.json (the cue table flattened
    /// by Scripts/build_haptic_import.py: each variant &lt;cue&gt;:&lt;variant&gt; with what it inherits, as the game cue
    /// &lt;cue&gt;_&lt;variant&gt;) into BoxingEventMap and the scene's BoxingFeedback: per game cue its stream clips
    /// (Assets/Boxing/Haptics/stream-clips/&lt;clip&gt;.wav), route gain and clip intensities (cached manifest
    /// intensity, so the effective gain = gain x intensity as with a Kit) into left_&lt;cue&gt; and right_&lt;cue&gt;
    /// (wrist cues) or &lt;cue&gt; (hit_body, neck) for the first clip and &lt;entry&gt;_2, _3 ... for the others, one
    /// trigger each in BoxingFeedback.impactHaptics with the pick (the game picks one clip per impact), and its
    /// sounds (Assets/Boxing/Haptics/sounds/&lt;Name&gt;.wav, none when empty), their volumes and the pick
    /// (random / roundRobin) into BoxingFeedback.cueSounds. The clips and sounds get the import settings of
    /// BoxingContent.ImportAudio (decompressed PCM; the SDK reads the clips' samples).
    /// Only what differs is written, so importing the checked-in table changes nothing.
    ///
    /// Run: Unity -batchmode -quit -nographics -projectPath &lt;project&gt; -executeMethod Hapbeat.Boxing.Editor.BoxingHapticImport.Run
    /// </summary>
    public static class BoxingHapticImport
    {
        public const string MapPath = "Assets/Boxing/Haptics/BoxingEventMap.asset";

        [Serializable] private sealed class Plan { public Item[] items; }
        [Serializable] private sealed class Item { public string cue; public string[] clips; public float gain; public float[] intensities; public string[] sounds; public float[] volumes; public string pick; }

        /// <summary>The cue table's name of a game cue: punch_glove_soft -&gt; punch_glove:soft.</summary>
        public static string TableCue(string gameCue)
        {
            int split = gameCue.LastIndexOf('_');
            return gameCue.Substring(0, split) + ":" + gameCue.Substring(split + 1);
        }

        /// <summary>The EventMap entry of a cue's k-th clip (0-based) at one place: the place's entry, then &lt;entry&gt;_2, _3 ...</summary>
        public static string EntryName(string placeEntry, int clip) => clip == 0 ? placeEntry : placeEntry + "_" + (clip + 1);

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
            int wrist = BoxingFeedback.WristCues, places = wrist + BoxingFeedback.Cues.Length;
            int entries = 0, mapChanges = 0, sceneChanges = 0;
            if (feedback.impactHaptics == null || feedback.impactHaptics.Length != places) { feedback.impactHaptics = new CueHapticSet[places]; sceneChanges++; }
            // The trigger objects by the entry they play; the ones no clip uses (and stale ones) are removed at the end.
            var unused = new Dictionary<string, HapbeatUnityEventTrigger>(); var stale = new List<HapbeatUnityEventTrigger>();
            foreach (var trigger in BoxingContent.ImpactTriggerObjects(feedback))
            {
                var played = trigger.ResolveEntry();
                if (played != null && !unused.ContainsKey(played.eventName)) unused[played.eventName] = trigger;
                else stale.Add(trigger);
            }
            var placeEntries = new List<HapbeatEventEntry>(); var furtherEntries = new List<HapbeatEventEntry>();
            foreach (var item in plan.items)
            {
                int cue = Array.IndexOf(BoxingFeedback.Cues, item.cue);
                if (cue < 0) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: unknown cue " + item.cue);
                if (item.clips.Length == 0 || item.intensities.Length != item.clips.Length) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: " + item.cue + " clips");
                var pick = item.pick == "roundRobin" ? CueSoundPick.RoundRobin : item.pick == "random" ? CueSoundPick.Random
                    : throw new Exception("HAPTIC_KIT_IMPORT_FAILED: " + item.cue + " pick " + item.pick);
                var clips = new AudioClip[item.clips.Length];
                for (int k = 0; k < clips.Length; k++)
                {
                    clips[k] = BoxingContent.ImportAudio(BoxingContent.StreamClips + item.clips[k] + ".wav");
                    if (clips[k] == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: clip " + item.clips[k] + " missing");
                }
                // BoxingFeedback.TriggerIndex: left_<cue>, right_<cue> (wrists) or <cue> (neck).
                foreach (int place in cue < wrist ? new[] { cue, wrist + cue } : new[] { wrist + cue })
                {
                    string placeName = (place < wrist ? "left_" : place < 2 * wrist ? "right_" : "") + item.cue;
                    var first = map.entries.Find(e => e != null && e.eventName == placeName);
                    if (first == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: " + MapPath + " has no entry " + placeName);
                    var triggers = new HapbeatUnityEventTrigger[clips.Length];
                    for (int k = 0; k < clips.Length; k++)
                    {
                        string name = EntryName(placeName, k);
                        var entry = k == 0 ? first : map.entries.Find(e => e != null && e.eventName == name);
                        if (entry == null)
                        {
                            entry = new HapbeatEventEntry { displayName = name, category = first.category, eventName = name, mode = HapticMode.StreamClip, target = first.target };
                            map.entries.Add(entry); mapChanges++;
                        }
                        if (entry.streamClip != clips[k] || entry.loop || !Mathf.Approximately(entry.gain, item.gain) || entry.target != first.target
                            || !Mathf.Approximately(entry.CachedManifestIntensity, item.intensities[k]) || entry.manifestOverride != null)
                        {
                            entry.streamClip = clips[k]; entry.loop = false; entry.gain = item.gain; entry.target = first.target;
                            // The intensity lives in the cue table, not in a Kit manifest.
                            entry.manifestOverride = null; entry.SetCachedManifestIntensity(item.intensities[k]);
                            mapChanges++;
                        }
                        (k == 0 ? placeEntries : furtherEntries).Add(entry);
                        if (unused.TryGetValue(name, out triggers[k])) unused.Remove(name);
                        else { triggers[k] = BoxingContent.AddImpactTrigger(feedback, map, entry); sceneChanges++; }
                        entries++;
                        Debug.Log("HAPTIC_KIT_IMPORT " + name + " <- " + item.clips[k] + " gain=" + item.gain + " intensity=" + item.intensities[k]);
                    }
                    var set = feedback.impactHaptics[place];
                    if (set == null || set.pick != pick || set.triggers == null || !set.triggers.SequenceEqual(triggers))
                    {
                        feedback.impactHaptics[place] = new CueHapticSet { triggers = triggers, pick = pick };
                        sceneChanges++;
                    }
                }
                Debug.Log("HAPTIC_KIT_IMPORT clips " + item.cue + " (" + TableCue(item.cue) + ") count=" + clips.Length + " pick=" + item.pick);
                var sounds = new AudioClip[item.sounds.Length];
                for (int i = 0; i < sounds.Length; i++)
                {
                    sounds[i] = BoxingContent.ImportAudio(BoxingContent.CueSounds + item.sounds[i] + ".wav");
                    if (sounds[i] == null) throw new Exception("HAPTIC_KIT_IMPORT_FAILED: sound " + item.sounds[i] + " missing");
                }
                var soundSet = feedback.cueSounds[cue];
                bool same = soundSet != null && soundSet.pick == pick && soundSet.clips.Length == sounds.Length && soundSet.volumes.Length == sounds.Length;
                for (int i = 0; same && i < sounds.Length; i++) same = soundSet.clips[i] == sounds[i] && Mathf.Approximately(soundSet.volumes[i], item.volumes[i]);
                if (!same)
                {
                    feedback.cueSounds[cue] = new CueSoundSet { clips = sounds, volumes = (float[])item.volumes.Clone(), pick = pick };
                    sceneChanges++;
                }
                for (int i = 0; i < sounds.Length; i++) Debug.Log("HAPTIC_KIT_IMPORT sound " + item.cue + " <- " + item.sounds[i] + " volume=" + item.volumes[i]);
                Debug.Log("HAPTIC_KIT_IMPORT sounds " + item.cue + " count=" + sounds.Length + " pick=" + item.pick);
            }
            // The place entries in BoxingContent's order (TriggerIndex), then the further clips; nothing else.
            var order = placeEntries.OrderBy(e => map.entries.IndexOf(e)).Concat(furtherEntries).ToList();
            if (!map.entries.SequenceEqual(order)) { map.entries.Clear(); map.entries.AddRange(order); mapChanges++; }
            foreach (var trigger in unused.Values.Concat(stale)) { UnityEngine.Object.DestroyImmediate(trigger.gameObject); sceneChanges++; }
            if (mapChanges > 0) { EditorUtility.SetDirty(map); AssetDatabase.SaveAssets(); }
            if (sceneChanges > 0) { EditorUtility.SetDirty(feedback); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
            Debug.Log("HAPTIC_KIT_IMPORTED cues=" + plan.items.Length + " entries=" + entries + " changed=" + (mapChanges + sceneChanges));
        }
    }
}
