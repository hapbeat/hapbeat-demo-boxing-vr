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
            DemoPause.Resume(); // restores AudioListener.pause
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
            // The panel follows the final bell after about a second, without waiting for the bell and voice to end.
            for (int i = 0; i < 110; i++) game.Simulate(.01f, pose);
            Assert.That(game.menu.IsOpen, Is.False, "The session replaces the automatic round menu.");
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            Assert.That(game.InputSuspended, Is.True);
            // The completion panel is pressed with the Demo Hub hands, not the gloves.
            game.input.SelectMode(BoxingInputMode.Hands); game.Simulate(.01f, pose);
            var panelHands = Object.FindFirstObjectByType<DemoHands>(FindObjectsInactive.Include);
            Assert.That(panelHands != null && panelHands.gameObject.activeSelf, Is.True);
            Assert.That(game.presentation.leftGlove.gameObject.activeSelf || game.presentation.rightGlove.gameObject.activeSelf, Is.False);

            DemoSession.RetryFromCompletion();
            Assert.That(panelHands.gameObject.activeSelf, Is.False);
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

        [Test] public void NormalLaunchRestartsTheRoundItself()
        {
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            Assert.That(game.HoldResultsForSession, Is.False);
            Assert.That(game.tuning.roundSeconds, Is.EqualTo(original.roundSeconds));
        }

        // The tutorial's own steps and its countdown to the round: BoxingTutorialTests.
        [Test] public void StartupBeginsTheTutorialWithoutAMenuAfterAShortReadyTime()
        {
            var pose = BoxingSceneTests.Pose();
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready));
            Assert.That(game.menu.IsOpen, Is.False);
            game.InputSuspended = true;
            for (int i = 0; i < Mathf.CeilToInt(game.tuning.autoStartSeconds * 100) + 50; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready), "A pause holds the start.");
            game.InputSuspended = false;
            for (int i = 0; i < Mathf.CeilToInt(game.tuning.autoStartSeconds * 100) - 30; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready), "The player gets a moment before the countdown.");
            for (int i = 0; i < 60; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Assert.That(game.TutorialStep, Is.EqualTo(1));
            Assert.That(game.menu.IsOpen, Is.False);
            for (int i = 0; i < 350; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial), "The tutorial runs before the countdown.");
            Assert.That(game.feedback.Rings, Is.Zero, "The gong waits for the round.");
        }

        [Test] public void SharedPauseFreezesTheRoundButTheGlovesFollowAndRemoteControlsUseIt()
        {
            DemoPause.Configure(null);
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            var pose = BoxingSceneTests.Pose();
            game.StartRound();
            for (int i = 0; i < 500; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));

            var open = DemoSwitchRuntime.ResolveControls("menu_open", "", null);
            Assert.That(open.CanExecuteControl("menu_open", ""), Is.True, "Boxing has no IDemoAppControls: the shared pause answers.");
            Run(open.ExecuteControl("menu_open", ""));
            Assert.That(DemoPause.IsPaused && game.InputSuspended, Is.True);
            Assert.That(game.feedback.hapticsEnabled, Is.False);
            float time = game.Round.TimeLeft; Vector3 enemy = game.Opponent.Left;
            for (int i = 0; i < 200; i++) game.Simulate(.01f, pose);
            Assert.That(game.Paused, Is.True);
            Assert.That(game.Round.TimeLeft, Is.EqualTo(time)); Assert.That(game.Opponent.Left, Is.EqualTo(enemy));
            pose.left += Vector3.up * .2f; game.Simulate(.01f, pose);
            Assert.That(Vector3.Distance(game.presentation.leftGlove.position, pose.left), Is.LessThan(.0001f), "Hands keep moving while paused.");

            Run(DemoSwitchRuntime.ResolveControls("menu_close", "", null).ExecuteControl("menu_close", ""));
            Assert.That(DemoPause.IsPaused || game.InputSuspended, Is.False);
            Assert.That(game.feedback.hapticsEnabled, Is.True);
            for (int i = 0; i < 100; i++) game.Simulate(.01f, pose);
            Assert.That(game.Round.TimeLeft, Is.LessThan(time));

            DemoPause.Pause();
            Run(DemoSwitchRuntime.ResolveControls("restart", "", null).ExecuteControl("restart", ""));
            Assert.That(DemoPause.IsPaused || game.InputSuspended, Is.False);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            Assert.That(game.Round.TimeLeft, Is.EqualTo(game.tuning.roundSeconds));
        }

        [Test] public void FocusLossOpensTheSharedPauseInsteadOfTheRoundMenu()
        {
            DemoPause.Configure(null);
            var host = game.gameObject.AddComponent<BoxingSessionHost>();
            DemoSession.RegisterHost(host);
            game.StartRound();
            game.PauseForExternalTransition();
            Assert.That(DemoPause.IsPaused, Is.True);
            Assert.That(game.menu.IsOpen, Is.False);
            game.menu.Open(); DemoPause.Resume(); DemoPause.Pause();
            Assert.That(game.menu.IsOpen, Is.False, "The developer menu never stays open under the shared pause.");
        }

        private static void Run(System.Collections.IEnumerator routine) { while (routine.MoveNext()) { } }
    }
}
