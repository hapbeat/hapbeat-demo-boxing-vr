using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The startup recenter (BoxingInput.TickStartup): once the visitor stands still after launch. Silent, no Hapbeat output.
    public sealed class BoxingStartupTests
    {
        private BoxingGame game;
        private BoxingTuning original;

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
        }
        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        private static BoxerPose At(float head, float sway = 0, float yaw = 0)
        {
            var pose = BoxingSceneTests.Pose(); pose.head.y = head; pose.head.x = sway; pose.headRotation = Quaternion.Euler(0, yaw, 0);
            return pose;
        }
        private int frame;
        // The head swaying 0.04 m (about 0.25 m/s) and turning 15 deg (about 100 deg/s): never still.
        private BoxerPose Moving(float head) { frame++; float s = Mathf.Sin(frame * .06f); return At(head, .04f * s, 15 * s); }
        private void Step(BoxerPose pose) { game.input.SetTestPose(pose); game.Simulate(.01f, pose); }

        [Test] public void RecentersOnceWhenStillAndSeedsTheStandingHeightBeforeTheTutorial()
        {
            Assert.That(game.input.StartupRecentered, Is.False);
            for (int i = 0; i < 300; i++) Step(Moving(1.72f));
            Assert.That(game.input.StartupRecentered, Is.False, "Not while the head moves.");
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready), "The tutorial waits for the startup recenter.");
            int still = 0;
            while (!game.input.StartupRecentered && still < 200) { Step(At(1.72f)); still++; }
            Assert.That(game.input.StartupRecentered, Is.True);
            Assert.That(still * .01f, Is.InRange(BoxingInput.StartupStillSeconds - .02f, BoxingInput.StartupStillSeconds + .03f), "After 0.5 s still.");
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready));
            Assert.That(game.input.ReferenceEyeHeight, Is.EqualTo(1.72f).Within(1e-4f));
            Assert.That(game.input.StandingEyeHeight, Is.EqualTo(1.72f).Within(1e-4f), "The startup head height seeds the standing eye height.");
            Assert.That(game.Opponent.Head.y, Is.EqualTo(1.72f).Within(1e-4f), "The opponent's head at the player's height.");
            // The tutorial follows autoStartSeconds after it (and the tracking settle), never before.
            int ready = 0;
            while (game.Round.Phase == BoxingPhase.Ready && ready < 400) { Step(At(1.72f)); ready++; }
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Assert.That(ready * .01f, Is.GreaterThanOrEqualTo(game.tuning.autoStartSeconds));
            // The running maximum continues from the seed.
            for (int i = 0; i < 50; i++) Step(At(1.8f));
            Assert.That(game.input.StandingEyeHeight, Is.EqualTo(1.8f).Within(1e-4f));
            Assert.That(game.feedback.Sends, Is.Zero);
        }

        [Test] public void RecentersAnywayAfterTheTimeout()
        {
            int frames = 0;
            while (!game.input.StartupRecentered && frames < 800) { Step(Moving(1.6f)); frames++; }
            Assert.That(game.input.StartupRecentered, Is.True);
            Assert.That(frames * .01f, Is.EqualTo(BoxingInput.StartupTimeout).Within(.02f));
        }

        [Test] public void UntrackedFramesDoNotCountAndASpinIsNotStill()
        {
            var lost = At(1.65f); lost.valid = false;
            for (int i = 0; i < 700; i++) Step(lost);
            Assert.That(game.input.StartupRecentered, Is.False, "The timeout counts tracked time only.");
            // A slow turn (30 deg/s) with a still head is not still either.
            for (int i = 0; i < 100; i++) Step(At(1.65f, 0, i * .3f));
            Assert.That(game.input.StartupRecentered, Is.False);
        }
    }
}
