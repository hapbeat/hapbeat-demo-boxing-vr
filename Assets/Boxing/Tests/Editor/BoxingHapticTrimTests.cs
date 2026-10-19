using System.IO;
using System.Linq;
using Hapbeat.DemoSwitch;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.Boxing.Tests
{
    // The demo-wide haptics trim (Demo Session option haptics_db, scope device; BoxingTuning.hapticsTrimDb as the fallback):
    // computed only, nothing is sent.
    public sealed class BoxingHapticTrimTests
    {
        private BoxingGame game;
        private BoxingTuning original;

        [SetUp] public void Setup()
        {
            DemoSession.ResetForTests();
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
        }
        [TearDown] public void Cleanup()
        {
            DemoSession.ResetForTests();
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        private HapbeatUnityEventTrigger Trigger(BoxingImpact impact) => game.feedback.impactHaptics[BoxingFeedback.TriggerIndex(impact)].triggers[0];
        private float Sent(BoxingImpact impact, float db) { game.feedback.HapticsTrimDb = db; return game.feedback.SentPeak(impact, Trigger(impact)); }

        [Test] public void DefaultTrimIsMinusThreeDecibelsInThreeDecibelStepsToMinus24()
        {
            Assert.That(original.hapticsTrimDb, Is.EqualTo(-3));
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-3));
            Assert.That(BoxingFeedback.TrimSteps, Is.EqualTo(new[] { 0, -3, -6, -9, -12, -15, -18, -24 }));
        }

        [Test] public void SixDecibelsHalveAStrongAndAQuietCueAndNothingExceedsFullScale()
        {
            // The neck's hard cue at full speed: capped at full scale.
            var strong = new BoxingImpact(ImpactZone.Head, 6, false, Vector3.zero, game.tuning, ImpactSurface.Body);
            Assert.That(BoxingFeedback.CueName(strong), Is.EqualTo("hit_body_hard"));
            float full = Sent(strong, 0), half = Sent(strong, -6);
            Debug.Log($"BOXING_HAPTIC_TRIM hit_body_hard 0 dB {full:F3}, -3 dB {Sent(strong, -3):F3}, -6 dB {half:F3}, -24 dB {Sent(strong, -24):F3}");
            Assert.That(half / full, Is.EqualTo(Mathf.Pow(10, -6 / 20f)).Within(1e-3f));
            Assert.That(Sent(strong, -24) / full, Is.EqualTo(Mathf.Pow(10, -24 / 20f)).Within(1e-3f));
            // A slow received hit on the head: the neck's soft cue at the minimum gain, well under full scale.
            var quiet = new BoxingImpact(ImpactZone.Head, .3f, false, Vector3.zero, game.tuning, ImpactSurface.Body);
            Assert.That(BoxingFeedback.CueName(quiet), Is.EqualTo("hit_body_soft"));
            float quietFull = Sent(quiet, 0);
            Assert.That(quietFull, Is.LessThan(1));
            Assert.That(Sent(quiet, -6) / quietFull, Is.EqualTo(Mathf.Pow(10, -6 / 20f)).Within(1e-3f));
            // No cue at any strength sends above full scale at 0 dB.
            foreach (var surface in new[] { ImpactSurface.Glove, ImpactSurface.Body })
            foreach (float strength in new[] { 0f, .5f, 1f })
            {
                var own = new BoxingImpact(ImpactZone.RightGlove, 4, Vector3.zero, game.tuning, surface, strength);
                Assert.That(Sent(own, 0), Is.LessThanOrEqualTo(1 + 1e-4f), BoxingFeedback.CueName(own));
            }
            foreach (var zone in new[] { ImpactZone.LeftGlove, ImpactZone.Head })
            foreach (float speed in new[] { .5f, 3, 6 })
            {
                var received = new BoxingImpact(zone, speed, false, Vector3.zero, game.tuning, zone == ImpactZone.Head ? ImpactSurface.Body : ImpactSurface.Glove);
                Assert.That(Sent(received, 0), Is.LessThanOrEqualTo(1 + 1e-4f), BoxingFeedback.CueName(received));
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }

        // punch_body (soft / hard), guard_glove hard and guard_arm hard (clips punch_body_t150_a / punch_body_hard_t151_b,
        // intensity 0.18): were capped at full scale (intensity 1 x route gain 2), now about a third of it at 0 dB.
        [Test] public void TheDamageWristCuesSendAboutAThirdOfTheFullScaleCap()
        {
            var impacts = new[]
            {
                new BoxingImpact(ImpactZone.LeftGlove, 1, Vector3.zero, game.tuning, ImpactSurface.Body, game.tuning.punchHardStrength - .01f),
                new BoxingImpact(ImpactZone.LeftGlove, 4, Vector3.zero, game.tuning, ImpactSurface.Body, 1),
                new BoxingImpact(ImpactZone.LeftGlove, 6, false, Vector3.zero, game.tuning, ImpactSurface.Glove),
                new BoxingImpact(ImpactZone.RightGlove, 6, false, Vector3.zero, game.tuning, ImpactSurface.Glove, ImpactPart.Forearm),
            };
            Assert.That(impacts.Select(BoxingFeedback.CueName), Is.EqualTo(new[] { "punch_body_soft", "punch_body_hard", "guard_glove_hard", "guard_arm_hard" }));
            foreach (var impact in impacts)
            {
                var entry = Trigger(impact).ResolveEntry();
                Assert.That(entry.streamClip.name, Does.StartWith("punch_body"), BoxingFeedback.CueName(impact));
                Assert.That(entry.GetEffectiveGain(), Is.EqualTo(2 * 0.18f).Within(1e-4f), BoxingFeedback.CueName(impact));
                float sent = Sent(impact, 0);
                Debug.Log($"BOXING_DAMAGE_CUE {BoxingFeedback.CueName(impact)} clip={entry.streamClip.name} gain={game.feedback.HapticGain(impact):F2} sent0dB={sent:F3} FS");
                // A third of the former full-scale cap (about -9.5 dB): 0.36 x the impact's share (0.8..1) x the clip's peak.
                Assert.That(sent, Is.InRange(0.2f, 0.4f), BoxingFeedback.CueName(impact));
            }
            // The neck and the t146 clips keep their level.
            var neck = new BoxingImpact(ImpactZone.Head, 6, false, Vector3.zero, game.tuning, ImpactSurface.Body);
            Assert.That(Trigger(neck).ResolveEntry().GetEffectiveGain(), Is.GreaterThan(.36f));
        }

        [Test] public void AStoredTrimThatIsNoLongerAStepFallsBackToTheDefaultWithAWarning()
        {
            var store = new MemoryDemoDeviceOptionStore();
            store.Set("boxing", "haptics_db", "2");  // -2 dB, a step before the 3 dB steps
            DemoSession.ResetForTests(deviceOptions: store);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("haptics_db.*2.*default 3"));
            DemoSession.Initialize("boxing", new SafeDemoSessionPlatform());
            game.Initialize();
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("3"));
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-3));
        }

        [Test] public void TheSessionOptionSetsTheTrim()
        {
            Assert.That(DemoSessionDescriptor.TryParse(File.ReadAllText("Assets/StreamingAssets/hapbeat-demo-session.json"), out var descriptor, out var error), Is.True, error);
            var option = descriptor.FindOption("haptics_db");
            Assert.That(option.Default, Is.EqualTo("3"));
            Assert.That(option.IsDevice, Is.True, "This headset's per-demo setting, not a preset's.");
            Assert.That(option.Values.Select(v => v.Value), Is.EqualTo(new[] { "0", "3", "6", "9", "12", "15", "18", "24" }));
            Assert.That(option.Values.Select(v => v.Label.Ja), Is.EqualTo(new[] { "0 dB", "-3 dB", "-6 dB", "-9 dB", "-12 dB", "-15 dB", "-18 dB", "-24 dB" }));
            string ticket = "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a57\",\"index\":0,\"haptics_ui\":true,\"steps\":[" +
                "{\"demo_id\":\"boxing\",\"title\":\"Boxing\",\"package\":\"com.hapbeat.boxing\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":{\"round\":\"55\",\"haptics_db\":\"6\"},\"retry\":true}]," +
                "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";
            Assert.That(DemoSession.TryBegin(ticket, "boxing", descriptor, out error), Is.True, error);
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-6));
            Assert.That(game.tuning.hapticsTrimDb, Is.EqualTo(-3), "The tuning asset is only the fallback and stays.");
            game.Initialize();
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-6), "A recenter keeps the session's trim.");
        }

        [Test] public void ALaunchWithoutATicketUsesTheTrimKeptFromTheLastTicket()
        {
            var store = new MemoryDemoDeviceOptionStore();
            // No ticket ever: the descriptor default.
            DemoSession.ResetForTests(deviceOptions: store);
            DemoSession.Initialize("boxing", new SafeDemoSessionPlatform());
            game.Initialize();
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-3));

            Assert.That(DemoSessionDescriptor.TryParse(File.ReadAllText("Assets/StreamingAssets/hapbeat-demo-session.json"), out var descriptor, out var error), Is.True, error);
            string ticket = "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a58\",\"index\":0,\"haptics_ui\":false,\"steps\":[" +
                "{\"demo_id\":\"boxing\",\"title\":\"Boxing\",\"package\":\"com.hapbeat.boxing\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":{\"round\":\"30\",\"haptics_db\":\"9\"},\"retry\":true}]," +
                "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";
            DemoSession.ResetForTests(deviceOptions: store);
            Assert.That(DemoSession.TryBegin(ticket, "boxing", descriptor, out error), Is.True, error);

            // Started again from Quest's app list (or Demo Switch SWITCH): no ticket, the kept -9 dB.
            DemoSession.ResetForTests(deviceOptions: store);
            DemoSession.Initialize("boxing", new SafeDemoSessionPlatform());
            Assert.That(DemoSession.IsActive, Is.False);
            game.Initialize();
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-9));
            Assert.That(BoxingGame.HapticsTrimDb("7", -3), Is.EqualTo(-3), "Not a trim step: the fallback.");
            Assert.That(BoxingGame.HapticsTrimDb("2", -3), Is.EqualTo(-3), "-2 dB is no longer a step.");
            Assert.That(BoxingGame.HapticsTrimDb("24", -3), Is.EqualTo(-24));
            Assert.That(BoxingGame.HapticsTrimDb(null, -4), Is.EqualTo(-4));
        }
    }
}
