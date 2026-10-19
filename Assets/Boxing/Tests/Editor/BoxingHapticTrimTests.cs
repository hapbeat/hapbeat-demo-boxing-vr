using System.IO;
using Hapbeat.DemoSwitch;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

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

        [Test] public void DefaultTrimIsMinusTwoDecibels()
        {
            Assert.That(original.hapticsTrimDb, Is.EqualTo(-2));
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-2));
        }

        [Test] public void SixDecibelsHalveASaturatingAndAQuietCueAndNothingExceedsFullScale()
        {
            // punch_body hard: entry gain 2 x a full-strength punch, far above full scale before the cap.
            var hard = new BoxingImpact(ImpactZone.LeftGlove, 4, Vector3.zero, game.tuning, ImpactSurface.Body, 1);
            Assert.That(BoxingFeedback.CueName(hard), Is.EqualTo("punch_body_hard"));
            var entry = Trigger(hard).ResolveEntry();
            Assert.That(entry.GetEffectiveGain() * game.feedback.HapticGain(hard) * game.feedback.ClipPeak(entry.streamClip), Is.GreaterThan(1), "It saturated before.");
            float full = Sent(hard, 0), half = Sent(hard, -6);
            Debug.Log($"BOXING_HAPTIC_TRIM punch_body_hard uncapped {entry.GetEffectiveGain() * game.feedback.HapticGain(hard) * game.feedback.ClipPeak(entry.streamClip):F2} FS, 0 dB {full:F3}, -2 dB {Sent(hard, -2):F3}, -6 dB {half:F3}");
            Assert.That(full, Is.EqualTo(1).Within(1e-3f), "0 dB: capped at full scale.");
            Assert.That(half / full, Is.EqualTo(Mathf.Pow(10, -6 / 20f)).Within(1e-3f));
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

        [Test] public void TheSessionOptionSetsTheTrim()
        {
            Assert.That(DemoSessionDescriptor.TryParse(File.ReadAllText("Assets/StreamingAssets/hapbeat-demo-session.json"), out var descriptor, out var error), Is.True, error);
            var option = descriptor.FindOption("haptics_db");
            Assert.That(option.Default, Is.EqualTo("2"));
            Assert.That(option.IsDevice, Is.True, "This headset's per-demo setting, not a preset's.");
            foreach (var value in new[] { "0", "2", "4", "6", "9", "12" }) Assert.That(option.HasValue(value), Is.True, value);
            string ticket = "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a57\",\"index\":0,\"haptics_ui\":true,\"steps\":[" +
                "{\"demo_id\":\"boxing\",\"title\":\"Boxing\",\"package\":\"com.hapbeat.boxing\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":{\"round\":\"55\",\"haptics_db\":\"6\"},\"retry\":true}]," +
                "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";
            Assert.That(DemoSession.TryBegin(ticket, "boxing", descriptor, out error), Is.True, error);
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-6));
            Assert.That(game.tuning.hapticsTrimDb, Is.EqualTo(-2), "The tuning asset is only the fallback and stays.");
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
            Assert.That(game.feedback.HapticsTrimDb, Is.EqualTo(-2));

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
            Assert.That(BoxingGame.HapticsTrimDb("7", -2), Is.EqualTo(-2), "Not a trim step: the fallback.");
            Assert.That(BoxingGame.HapticsTrimDb(null, -4), Is.EqualTo(-4));
        }
    }
}
