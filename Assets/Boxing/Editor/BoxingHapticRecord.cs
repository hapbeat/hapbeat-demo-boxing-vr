using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Hapbeat.Boxing.Editor
{
    /// <summary>
    /// Step 1 of the haptic authoring tool (repos-tools/hapbeat-haptic-authoring) for Boxing: a PC replay of
    /// every cue of the cue table (Assets/Boxing/Haptics/boxing-cues.json), three takes of each, rendered at a
    /// fixed 30 fps from the player's eye (no sound, nothing sent to Hapbeat devices). The game classifies each
    /// contact itself (BoxingGame, BoxingFeedback.CueIndex): the player's gloves follow a scripted punch, the
    /// opponent strikes through its recording hook (BoxingOpponent.ScriptedStrike; no AI meanwhile).
    /// A take that fires anything but its one intended cue (and hand / zone) fails the run.
    /// Writes Saved/HapticClips/Frames/f&lt;frame&gt;.png and Saved/HapticClips/record.json
    /// (fps, events [[frame, cue, hand, note, gain]], no levels: every cue is a one-shot);
    /// Scripts/haptic_session.py turns them into the Studio Scene tab's data.
    ///
    /// Run: Unity -batchmode -quit -projectPath &lt;project&gt; -executeMethod Hapbeat.Boxing.Editor.BoxingHapticRecord.Run (with graphics).
    /// </summary>
    public static class BoxingHapticRecord
    {
        public const int Fps = 30;
        /// <summary>Game steps per frame: the game runs at 90 Hz as on the headset.</summary>
        public const int SubSteps = 3;
        public const int Width = 1280;
        public const int Height = 720;
        public const float FieldOfView = 70f;
        /// <summary>The eye looks this far down, so the own gloves and the opponent are both in view.</summary>
        public const float PitchDegrees = 10f;
        public const float GapSeconds = 1f;
        public const float MoveSeconds = 0.6f;
        public const float PunchHoldSeconds = 0.15f, PunchReturnSeconds = 0.45f;
        /// <summary>Player punches: glove speed toward the target (= the contact's relative speed, the target barely moves).</summary>
        public const float SoftPunchSpeed = 1f, HardPunchSpeed = 4f;
        /// <summary>Opponent strikes: strike phase length x this (soft ≈ 1.2 m/s, hard ≈ 3.5–4.5 m/s at the contact).</summary>
        public const float SoftStrikeScale = 6.8f, HardStrikeScale = 2f;
        public const float StrikeTimeoutSeconds = 4f, StrikeSettleSeconds = 0.3f;
        public static readonly Vector3 PlayerHead = new Vector3(0, 1.65f, 0);

        private enum Stance { Ready, Guard, ArmGuard, Open }
        private enum Target { Glove, Head, Body }
        private sealed class Take
        {
            public string cue; public Stance stance; public bool left; public bool hard;
            // A player punch at a target, or an opponent strike of a kind.
            public bool punch; public Target target; public BoxingAttack attack;
            public string Label => punch ? (left ? "left" : "right") + " punch to the " + target.ToString().ToLowerInvariant()
                : (left ? "jab" : "cross") + (attack == BoxingAttack.BodyStraight ? " to the body" : " to the head")
                  + (stance == Stance.Guard ? " on the glove" : stance == Stance.ArmGuard ? " on the forearm" : "");
        }

        /// <summary>10 cues x 3 takes. punch_* at about 1 / 4 m/s, guard_* / hit_body slow / normal strikes.</summary>
        private static IEnumerable<Take> Script()
        {
            foreach (bool hard in new[] { false, true })
            {
                string level = hard ? "_hard" : "_soft";
                foreach (bool left in new[] { true, false, true })
                    yield return new Take { cue = "punch_glove" + level, stance = Stance.Ready, punch = true, left = left, hard = hard, target = Target.Glove };
                foreach (var (left, target) in new[] { (true, Target.Head), (false, Target.Body), (false, Target.Head) })
                    yield return new Take { cue = "punch_body" + level, stance = Stance.Ready, punch = true, left = left, hard = hard, target = target };
            }
            foreach (bool hard in new[] { false, true })
            {
                string level = hard ? "_hard" : "_soft";
                foreach (bool left in new[] { true, false, true })
                    yield return new Take { cue = "guard_glove" + level, stance = Stance.Guard, left = left, hard = hard, attack = BoxingAttack.HeadStraight };
                foreach (bool left in new[] { true, false, true })
                    yield return new Take { cue = "guard_arm" + level, stance = Stance.ArmGuard, left = left, hard = hard, attack = BoxingAttack.HeadStraight };
                foreach (var attack in hard ? new[] { BoxingAttack.BodyStraight, BoxingAttack.HeadStraight, BoxingAttack.BodyStraight }
                             : new[] { BoxingAttack.HeadStraight, BoxingAttack.BodyStraight, BoxingAttack.HeadStraight })
                    yield return new Take { cue = "hit_body" + level, stance = Stance.Open, left = attack == BoxingAttack.HeadStraight, hard = hard, attack = attack };
            }
        }

        private static BoxerPose StancePose(Stance stance)
        {
            Vector3 l, r; var rotation = Quaternion.identity;
            switch (stance)
            {
                case Stance.Guard: l = new Vector3(-.12f, 0, .3f); r = new Vector3(.12f, 0, .3f); break;
                // Gloves raised beside the face: the forearms hang across the line of a straight to the head.
                case Stance.ArmGuard: l = new Vector3(-.15f, .2f, .3f); r = new Vector3(.15f, .2f, .3f); rotation = Quaternion.LookRotation(Vector3.up, Vector3.back); break;
                case Stance.Open: l = new Vector3(-.42f, -.5f, .25f); r = new Vector3(.42f, -.5f, .25f); break;
                default: l = new Vector3(-.1f, -.12f, .28f); r = new Vector3(.1f, -.12f, .28f); break;
            }
            return new BoxerPose { valid = true, head = PlayerHead, headRotation = Quaternion.identity, left = PlayerHead + l, right = PlayerHead + r,
                leftRotation = rotation, rightRotation = rotation, leftClosed = true, rightClosed = true, headTracked = true, leftTracked = true, rightTracked = true };
        }

        public static void Run()
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outDir = Path.Combine(root, "Saved", "HapticClips");
            var frames = Path.Combine(outDir, "Frames");
            if (Directory.Exists(frames)) Directory.Delete(frames, true);
            Directory.CreateDirectory(frames);

            EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game = UnityEngine.Object.FindFirstObjectByType<BoxingGame>();
            // Silent: no PC audio and no Hapbeat output (the SDK root stays off; batch mode blocks both as well).
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false);
            game.Initialize(); game.menu.Close();
            game.Opponent.Scripted = true;
            // Long and without knockouts: the takes only need the fighting phase.
            game.Round.Start(900, 100000); game.Round.Tick(3, false);

            var camera = game.input.headCamera;
            camera.fieldOfView = FieldOfView;
            var target = new RenderTexture(Width, Height, 24); target.Create();
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
            // Editor render requests can reuse cached skinning matrices: the opponent is drawn from its bones
            // baked every frame (as BoxingAvatarSetup's stills), not by its skinned renderer.
            var skin = game.presentation.enemyAvatar.skin; var skinned = new Mesh();
            var baked = new GameObject("Haptic record skin"); baked.transform.SetParent(skin.transform, false);
            baked.AddComponent<MeshFilter>().sharedMesh = skinned; baked.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
            skin.enabled = false;
            int frame = 0;
            float dt = 1f / (Fps * SubSteps);
            var fired = new List<(int frame, BoxingImpact impact)>();
            // An impact belongs to the frame rendered after the step in which it fired.
            game.feedback.Reported += impact => fired.Add((frame, impact));

            // Plays `seconds` of the pose function (time since the segment start), SubSteps game steps per frame.
            void Play(float seconds, Func<float, BoxerPose> at)
            {
                for (float t = 0; t < seconds - 1e-4f; t += 1f / Fps)
                {
                    BoxerPose p = default;
                    for (int i = 1; i <= SubSteps; i++) { p = at(t + i * dt); game.input.SetTestPose(p); game.Simulate(dt, p); }
                    camera.transform.SetPositionAndRotation(p.head, Quaternion.Euler(PitchDegrees, 0, 0));
                    skin.BakeMesh(skinned);
                    var scale = skin.transform.localScale;
                    baked.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    var old = RenderTexture.active; RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    RenderTexture.active = old;
                    File.WriteAllBytes(Path.Combine(frames, "f" + frame.ToString("D5") + ".png"), pixels.EncodeToPNG());
                    frame++;
                }
            }
            var pose = StancePose(Stance.Ready);
            RenderPipeline.SubmitRenderRequest(camera, request);
            Play(GapSeconds, _ => pose); // tracking settles (HOLD STILL) before the first take

            var events = new List<string>();
            var failures = new List<string>();
            var byCue = new Dictionary<string, List<BoxingImpact>>();
            string Hand(BoxingImpact i) => BoxingFeedback.CueIndex(i) >= BoxingFeedback.WristCues ? "both" : i.zone == ImpactZone.LeftGlove ? "left" : "right";
            int number = 0;
            foreach (var take in Script())
            {
                number++;
                var stance = StancePose(take.stance);
                if (stance.left != pose.left || stance.right != pose.right || stance.leftRotation != pose.leftRotation)
                {
                    var from = pose;
                    Play(MoveSeconds, t => Blend(from, stance, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / MoveSeconds))));
                    pose = stance;
                }
                Play(GapSeconds, _ => pose);
                fired.Clear();
                if (take.punch) Punch(game, take, pose, Play); else Strike(game, take, pose, Play);
                Play(GapSeconds, _ => pose);

                bool ok = fired.Count == 1 && BoxingFeedback.CueName(fired[0].impact) == take.cue;
                if (ok && BoxingFeedback.CueIndex(fired[0].impact) < BoxingFeedback.WristCues) ok = Hand(fired[0].impact) == (take.left ? "left" : "right");
                if (ok && take.cue.StartsWith("hit_body")) ok = fired[0].impact.zone == (take.attack == BoxingAttack.BodyStraight ? ImpactZone.Body : ImpactZone.Head);
                foreach (var (at, impact) in fired)
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "HAPTIC_RECORD take={0} ({1}) intended={2} cue={3} hand={4} zone={5} speed={6:0.00} gain={7:0.000} frame={8} {9}",
                        number, take.Label, take.cue, BoxingFeedback.CueName(impact), Hand(impact), impact.zone, impact.relativeSpeed, impact.gain, at, ok ? "OK" : "MISMATCH"));
                if (!ok)
                {
                    failures.Add($"take {number} ({take.Label}): intended {take.cue}, fired [{string.Join(",", fired.Select(f => BoxingFeedback.CueName(f.impact) + "/" + f.impact.zone))}]");
                    continue;
                }
                var (hitFrame, hit) = fired[0];
                if (!byCue.TryGetValue(take.cue, out var list)) byCue[take.cue] = list = new List<BoxingImpact>();
                list.Add(hit);
                string note = string.Format(CultureInfo.InvariantCulture, "take {0}: {1}, {2:0.00} m/s, gain {3:0.00}", list.Count, take.Label, hit.relativeSpeed, hit.gain);
                events.Add(string.Format(CultureInfo.InvariantCulture, "[{0},\"{1}\",\"{2}\",\"{3}\",{4:0.###}]", hitFrame, take.cue, Hand(hit), note, hit.gain));
            }
            target.Release();
            skin.enabled = true; UnityEngine.Object.DestroyImmediate(baked); UnityEngine.Object.DestroyImmediate(skinned);
            if (failures.Count > 0) throw new Exception("HAPTIC_RECORD_FAILED: " + string.Join("; ", failures));
            foreach (var cue in BoxingFeedback.Cues)
            {
                var list = byCue[cue];
                Debug.Log(string.Format(CultureInfo.InvariantCulture, "HAPTIC_RECORD_CUE {0} takes={1} speed={2:0.00}..{3:0.00} gain={4:0.000}..{5:0.000}",
                    cue, list.Count, list.Min(i => i.relativeSpeed), list.Max(i => i.relativeSpeed), list.Min(i => i.gain), list.Max(i => i.gain)));
            }
            var json = new StringBuilder();
            json.Append("{\"fps\":").Append(Fps).Append(",\"level_columns\":[],\"events\":[")
                .Append(string.Join(",", events)).Append("],\"levels\":{},\"frames\":").Append(frame).Append('}');
            File.WriteAllText(Path.Combine(outDir, "record.json"), json.ToString());
            Debug.Log("HAPTIC_RECORD_COMPLETE frames=" + frame + " events=" + events.Count + " takes=" + number);
        }

        private static BoxerPose Blend(BoxerPose from, BoxerPose to, float s)
        {
            var p = to;
            p.left = Vector3.Lerp(from.left, to.left, s); p.right = Vector3.Lerp(from.right, to.right, s);
            p.leftRotation = Quaternion.Slerp(from.leftRotation, to.leftRotation, s); p.rightRotation = Quaternion.Slerp(from.rightRotation, to.rightRotation, s);
            return p;
        }

        /// <summary>
        /// A straight punch from the ready stance at constant speed until the glove is 6 cm inside the contact
        /// distance of the target (more than one game step at 4 m/s, so the contact step moves at full speed),
        /// a short hold, and the withdrawal that readies the next punch (BoxingPunch). Head punches aim 6 cm above
        /// the centre, clear of the opponent's gloves at rest.
        /// </summary>
        private static void Punch(BoxingGame game, Take take, BoxerPose rest, Action<float, Func<float, BoxerPose>> play)
        {
            var o = game.Opponent; var tuning = game.tuning;
            Vector3 aim = take.target == Target.Glove ? (take.left ? o.Left : o.Right) : take.target == Target.Head ? o.Head + Vector3.up * .06f : o.Body;
            float reach = tuning.gloveRadius - .06f + (take.target == Target.Glove ? tuning.enemyGloveRadius : take.target == Target.Head ? tuning.enemyHeadRadius : tuning.enemyBodyRadius);
            Vector3 start = take.left ? rest.left : rest.right;
            Vector3 end = aim + (start - aim).normalized * reach;
            float duration = Vector3.Distance(start, end) / (take.hard ? HardPunchSpeed : SoftPunchSpeed);
            BoxerPose At(Vector3 glove) { var p = rest; if (take.left) p.left = glove; else p.right = glove; return p; }
            play(duration, t => At(Vector3.Lerp(start, end, Mathf.Clamp01(t / duration))));
            play(PunchHoldSeconds, _ => At(end));
            play(PunchReturnSeconds, t => At(Vector3.Lerp(end, start, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / PunchReturnSeconds)))));
        }

        /// <summary>One opponent strike (jab = its left glove, cross = its right) at the held stance, until it has withdrawn.</summary>
        private static void Strike(BoxingGame game, Take take, BoxerPose stance, Action<float, Func<float, BoxerPose>> play)
        {
            int completed = game.Opponent.CompletedAttacks;
            game.Opponent.ScriptedStrike(take.attack, take.left, take.hard ? HardStrikeScale : SoftStrikeScale);
            for (float t = 0; game.Opponent.CompletedAttacks == completed; t += 1f / Fps)
            {
                if (t > StrikeTimeoutSeconds) throw new Exception("HAPTIC_RECORD_FAILED: the opponent's " + take.Label + " did not finish");
                play(1f / Fps, _ => stance);
            }
            play(StrikeSettleSeconds, _ => stance);
        }
    }
}
