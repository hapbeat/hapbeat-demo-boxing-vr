using System.IO;
using Hapbeat.DemoSwitch;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingSessionTests
    {
        private const string DescriptorPath = "Assets/StreamingAssets/hapbeat-demo-session.json";
        private BoxingGame game;
        private BoxingTuning original;

        private static string Ticket(string round) =>
            "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a56\",\"index\":0,\"haptics_ui\":true,\"steps\":[" +
            "{\"demo_id\":\"boxing\",\"title\":\"Boxing\",\"package\":\"com.hapbeat.boxing\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":{\"round\":\"" + round + "\"},\"retry\":true}]," +
            "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";

        private static DemoSessionDescriptor Descriptor()
        {
            Assert.That(DemoSessionDescriptor.TryParse(File.ReadAllText(DescriptorPath), out var descriptor, out var error), Is.True, error);
            return descriptor;
        }

        [SetUp] public void Setup()
        {
            DemoSession.ResetForTests();
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose());
        }

        [TearDown] public void Cleanup()
        {
            DemoSession.ResetForTests();
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        [Test] public void DescriptorDeclaresRoundAndHapticsToggle()
        {
            var descriptor = Descriptor();
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            Assert.That(descriptor.DemoId, Is.EqualTo(settings.CurrentDemoId));
            Assert.That(descriptor.SupportsHapticsToggle, Is.True);
            var round = descriptor.FindOption("round");
            Assert.That(round.Default, Is.EqualTo("90"));
            Assert.That(round.HasValue("60") && round.HasValue("90"), Is.True);
        }

        [Test] public void SessionRoundEndsInTheCompletionPanelAndRetryRestartsTheRound()
        {
            Assert.That(DemoSession.TryBegin(Ticket("60"), "boxing", Descriptor(), out var error), Is.True, error);
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.tuning.roundSeconds, Is.EqualTo(60));
            Assert.That(game.feedback.hapticsEnabled, Is.True, "Each step starts with haptics on.");

            var pose = BoxingSceneTests.Pose();
            game.StartRound();
            for (int i = 0; i < 350; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.TimeLeft, Is.LessThanOrEqualTo(60));
            game.Round.Tick(90, false); game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            for (int i = 0; i < Mathf.CeilToInt(game.ResultDuration * 100) + 30; i++) game.Simulate(.01f, pose);
            Assert.That(game.menu.IsOpen, Is.False, "The session replaces the automatic round menu.");
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            Assert.That(game.InputSuspended, Is.True);

            DemoSession.RetryFromCompletion();
            Assert.That(DemoSession.IsCompletionShown, Is.False);
            Assert.That(game.InputSuspended, Is.False);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            Assert.That(game.Round.TimeLeft, Is.EqualTo(60));
        }

        [Test] public void HapticsSwitchMirrorsTheMenuRowAndKeepsSound()
        {
            DemoSession.ResetForTests(null, "boxing", Descriptor());
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(DemoSession.HapticsControls.CanExecuteControl("haptics_off", ""), Is.True);
            DemoSession.SetHapticsEnabled(false);
            Assert.That(game.feedback.hapticsEnabled, Is.False);
            Assert.That(game.feedback.soundEnabled, Is.True);
            game.menu.Activate(5);
            Assert.That(game.feedback.hapticsEnabled, Is.True);
            Assert.That(DemoSession.HapticsEnabled, Is.True, "Menu row and session button share one state.");
        }

        [Test] public void NormalLaunchKeepsTheRoundMenu()
        {
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.HoldResultsForSession, Is.False);
            Assert.That(game.tuning.roundSeconds, Is.EqualTo(original.roundSeconds));
        }
    }
}
