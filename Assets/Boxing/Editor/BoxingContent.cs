using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hapbeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    // Authored geometry and haptic event wiring; source audio provenance is in THIRD_PARTY_NOTICES.md.
    public static class BoxingContent
    {
        private const string Root = "Assets/Boxing/";
        [MenuItem("Hapbeat Boxing/Simplify Haptic Events")]
        public static void SimplifyHapticEvents()
        {
            var scene=EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var feedback=UnityEngine.Object.FindFirstObjectByType<BoxingGame>().feedback;
            var map=AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(Root+"Haptics/BoxingEventMap.asset");
            bool Unused(string name)=>name=="head_glove_soft" || name=="head_glove_hard";
            var retained=new List<HapbeatUnityEventTrigger>();
            foreach(var trigger in feedback.impactTriggers)
            {
                var entry=trigger.ResolveEntry();
                if(Unused(entry.eventName)) UnityEngine.Object.DestroyImmediate(trigger.gameObject);
                else retained.Add(trigger);
            }
            map.entries.RemoveAll(e=>Unused(e.eventName));
            feedback.impactTriggers=retained.ToArray();
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(feedback);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate();
        }
        // One EventMap entry per cue and side (left_<cue> / right_<cue> at the wrists, <cue> at the neck) and the
        // cue sounds, at their defaults (the cue table Assets/Boxing/Haptics/boxing-cues.json as checked in).
        [MenuItem("Hapbeat Boxing/Rebuild Haptic Events")]
        public static void RebuildHapticEvents()
        {
            var scene=EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            ConfigureHapticEvents(UnityEngine.Object.FindFirstObjectByType<BoxingGame>());
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate();
        }
        [MenuItem("Hapbeat Boxing/Apply Reach And Punch Update")]
        public static void ApplyReachUpdate()
        {
            var scene = EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            game.input.startPoint.position = new Vector3(0, 0.003f, 0.2f);
            game.input.mode = BoxingInputMode.Hands;
            game.input.startPoint.rotation = Quaternion.identity;
            game.input.debugStickMovement = true;
            game.input.debugMoveSpeed = 0.45f;
            var scoreboard = (RectTransform)game.presentation.timerText.transform.parent;
            scoreboard.anchoredPosition3D = new Vector3(0, 2.7f, 3.05f);
            EditorUtility.SetDirty(scoreboard);
            game.input.Recenter();
            ConfigureModels(game);
            EditorUtility.SetDirty(game.input); EditorUtility.SetDirty(game.tuning);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate(); Preview();
        }
        [MenuItem("Hapbeat Boxing/Update Models And Surface Feedback")]
        public static void Upgrade()
        {
            var scene = EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            ConfigureModels(game); ConfigureImpactVisuals(game.presentation); ConfigureFeedback(game);
            game.tuning.gloveRadius = 0.07f; EditorUtility.SetDirty(game.tuning);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate();
            Preview();
        }
        public static void Preview()
        {
            BoxingProject.Validate();
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            var pose = new BoxerPose { valid = true, head = new Vector3(0, 1.65f, 0), left = new Vector3(-0.25f, 1.25f, 0.34f), right = new Vector3(0.25f, 1.25f, 0.34f),
                headRotation = Quaternion.identity, leftRotation = Quaternion.identity, rightRotation = Quaternion.identity, leftClosed = true, rightClosed = true };
            pose.head += game.input.StartPosition; pose.left += game.input.StartPosition; pose.right += game.input.StartPosition;
            game.menu.Close(); game.Round.Start(90); game.Round.Tick(3, false);
            var camera = game.input.headCamera; camera.transform.SetPositionAndRotation(pose.head, Quaternion.identity);
            game.presentation.Render(game, pose, true); Directory.CreateDirectory("Logs");
            BoxingVerification.Render(camera, "Logs/boxing-content-rest.png");
            for (int i = 0; i < 2000 && game.Opponent.GuardWeight < 0.99f; i++) game.Opponent.Tick(0.01f, pose.head, Quaternion.identity, true);
            game.presentation.Render(game, pose, true);
            BoxingVerification.Render(camera, "Logs/boxing-content-guard.png");
        }

        private static Material Material(string name, Color color, bool unlit = false)
        {
            string path = Root + "Art/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Particles/Unlit" : "Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, path); }
            material.SetColor("_BaseColor", color);
            if (unlit)
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", 5); material.SetFloat("_DstBlend", 10);
                material.SetFloat("_SrcBlendAlpha", 1); material.SetFloat("_DstBlendAlpha", 10); material.SetFloat("_ZWrite", 0);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.SetOverrideTag("RenderType", "Transparent"); material.renderQueue = 3000;
            }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.12f);
            EditorUtility.SetDirty(material); return material;
        }
        private static void Clear(Transform root)
        {
            // Only explicitly selected model/effect children are replaced; rig and gameplay objects stay intact.
            for (int i = root.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
        public static void ConfigureModels(BoxingGame game)
        {
            var v = game.presentation;
            BoxingAvatarSetup.Attach(v);
            BoxingPlayerGloves.Install(v);
            EditorUtility.SetDirty(v);
            Debug.Log("BOXING_GLOVE_BOUNDS " + GloveSize(v.leftGlove).ToString("F3"));
        }
        public static Vector3 GloveSize(Transform glove)
        {
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var filter in glove.GetComponentsInChildren<MeshFilter>())
            {
                var b = filter.sharedMesh.bounds;
                for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                    bounds.Encapsulate(glove.InverseTransformPoint(filter.transform.TransformPoint(b.center + Vector3.Scale(b.extents, new Vector3(x, y, z)))));
            }
            return bounds.size;
        }
        public static void ConfigureImpactVisuals(BoxingPresentation view)
        {
            var oldPanel = view.impactText.transform.parent.Find("Damage tint");
            if (oldPanel != null) UnityEngine.Object.DestroyImmediate(oldPanel.gameObject);
            Clear(view.hitBurst);
            var line = view.hitBurst.GetComponent<LineRenderer>();
            if (line == null) line = view.hitBurst.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = Material("ContactRing", Color.white, true);
            line.useWorldSpace = false; line.loop = true; line.widthMultiplier = 0.05f; line.positionCount = 24;
            for (int i = 0; i < 24; i++) { float a = i * Mathf.PI * 2 / 24; line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0)); }
            view.hitBurst.gameObject.SetActive(false);
        }

        public static void ConfigureFeedback(BoxingGame game)
        {
            Directory.CreateDirectory(Root + "Audio"); Directory.CreateDirectory(Root + "Haptics"); AssetDatabase.Refresh();
            var f = game.feedback;
            if (f.audioSource == null) f.audioSource = game.gameObject.AddComponent<AudioSource>();
            if (f.bellSource == null) f.bellSource = game.gameObject.AddComponent<AudioSource>();
            f.audioSource.playOnAwake = f.bellSource.playOnAwake = false;
            f.audioSource.spatialBlend = f.bellSource.spatialBlend = 0;
            var source = ImportAudio(Root + "Audio/BoxingBellSource.wav");
            ComposeGong(source, Root + "Audio/BoxingGongFour.wav");
            f.bell = ImportAudio(Root + "Audio/BoxingGongFour.wav");
            ConfigureHapticEvents(game);
        }
        public const string StreamClips = Root + "Haptics/stream-clips/", CueSounds = Root + "Haptics/sounds/";
        public const float DefaultEventGain = 0.8f;
        // Default cue sounds (Kenney Impact Sounds as WAV): glove contacts / body contacts x soft / hard (BoxingFeedback.Cues order).
        public static string DefaultSound(int cue) =>
            (cue < 2 || (cue >= 4 && cue < 8) ? "impactSoft_" : "impactPunch_") + (cue % 2 == 0 ? "medium" : "heavy") + "_000";
        // Replaces the EventMap entries and the scene's impact triggers (BoxingFeedback.TriggerIndex order):
        // stream clip stream-clips/<cue>.wav, gain 0.8, clip intensity 1, and the default cue sounds.
        public static void ConfigureHapticEvents(BoxingGame game)
        {
            var f = game.feedback;
            int cues = BoxingFeedback.Cues.Length, wrist = BoxingFeedback.WristCues;
            f.cueSounds = new AudioClip[cues]; f.cueSoundVolumes = new float[cues];
            for (int i = 0; i < cues; i++) { f.cueSounds[i] = ImportAudio(CueSounds + DefaultSound(i) + ".wav"); f.cueSoundVolumes[i] = 1; }
            foreach (var trigger in f.impactTriggers) if (trigger != null) UnityEngine.Object.DestroyImmediate(trigger.gameObject);
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(Root + "Haptics/BoxingEventMap.asset");
            if (map == null) { map = ScriptableObject.CreateInstance<HapbeatEventMap>(); AssetDatabase.CreateAsset(map, Root + "Haptics/BoxingEventMap.asset"); }
            map.entries.Clear(); f.impactTriggers = new HapbeatUnityEventTrigger[2 * wrist + cues - wrist];
            for (int i = 0; i < f.impactTriggers.Length; i++)
            {
                int cue = i < 2 * wrist ? i % wrist : i - wrist;
                string name = (i < wrist ? "left_" : i < 2 * wrist ? "right_" : "") + BoxingFeedback.Cues[cue];
                var entry = new HapbeatEventEntry { displayName = name, category = "boxing", eventName = name, mode = HapticMode.StreamClip,
                    streamClip = ImportAudio(StreamClips + BoxingFeedback.Cues[cue] + ".wav"), gain = DefaultEventGain,
                    target = i < wrist ? "*/pos_l_wrist" : i < 2 * wrist ? "*/pos_r_wrist" : "*/pos_neck" };
                // The clip intensity lives in the cue table, not in a Kit manifest (BoxingHapticImport).
                entry.SetCachedManifestIntensity(1);
                map.entries.Add(entry);
                var go = new GameObject("Haptic " + name); go.transform.SetParent(f.transform, false);
                f.impactTriggers[i] = go.AddComponent<HapbeatUnityEventTrigger>(); f.impactTriggers[i].EditorSetupEntry(map, entry.id);
            }
            EditorUtility.SetDirty(map); EditorUtility.SetDirty(f);
        }
        public static AudioClip ImportAudio(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Audio source missing: " + path);
            var settings = importer.defaultSampleSettings; settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM; settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
        private static void ComposeGong(AudioClip source, string path)
        {
            var data = new float[source.samples * source.channels];
            if (!source.GetData(data, 0)) throw new InvalidOperationException("Cannot decode CC0 boxing bell.");
            var mono = new float[source.samples];
            for (int i = 0; i < mono.Length; i++) for (int c = 0; c < source.channels; c++) mono[i] += data[i * source.channels + c] / source.channels;
            int window = source.frequency / 100;
            var energy = new float[mono.Length / window];
            for (int b = 0; b < energy.Length; b++) for (int i = 0; i < window; i++) energy[b] += mono[b * window + i] * mono[b * window + i];
            var peaks = new List<int>();
            foreach (int b in Enumerable.Range(1, energy.Length - 1).OrderByDescending(b => energy[b] - energy[b - 1]))
            {
                if (peaks.All(p => Math.Abs(p - b) > 30)) peaks.Add(b);
                if (peaks.Count == 3) break;
            }
            peaks.Sort();
            int start = Math.Max(0, peaks[2] - 1) * window;
            float[] offsets = { 0, 0.24f, 0.48f, 0.88f };
            var result = new float[mono.Length - start + (int)(source.frequency * offsets[3])];
            for (int hit = 0; hit < offsets.Length; hit++)
            {
                int offset = (int)(source.frequency * offsets[hit]);
                // Damp the first three hits so overlapping tails do not mask the four attacks.
                int count = hit == 3 ? mono.Length - start : Math.Min(mono.Length - start, (int)(source.frequency * (hit == 2 ? 0.32f : 0.20f)));
                for (int i = 0; i < count; i++)
                {
                    float attack = Mathf.Min(1, i / (source.frequency * 0.003f));
                    float release = hit == 3 ? 1 : Mathf.Min(1, (count - 1 - i) / (source.frequency * 0.025f));
                    result[offset + i] += mono[start + i] * attack * release * (hit == 3 ? 1 : 0.75f);
                }
            }
            float peak = result.Max(x => Mathf.Abs(x));
            for (int i = 0; i < result.Length; i++) result[i] *= 0.85f / Mathf.Max(peak, 0.001f) * Mathf.Min(1, (result.Length - 1 - i) / (source.frequency * 0.08f));
            WriteWave(path, source.frequency, result);
            Debug.Log("BOXING_GONG: source strikes at " + string.Join(",", peaks.Select(p => (p / 100f).ToString("F2"))) + "; four strikes at 0,0.24,0.48,0.88s");
        }
        private static void WriteWave(string path, int rate, float[] samples)
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Length * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples.Length * 2);
            foreach (float value in samples) writer.Write((short)(Mathf.Clamp(value, -1, 1) * 32767));
        }
    }
}
