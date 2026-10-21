using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // When the game waits because the hands are not seen, the board says so in Japanese in every phase. Silent, no Hapbeat output.
    public sealed class BoxingHandsMissingTests
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

        private string Board => game.presentation.timerText.text;
        // The headset tracked, both hands lost.
        private static BoxerPose NoHands(BoxerPose pose) { pose.valid = false; pose.headTracked = true; pose.leftTracked = pose.rightTracked = false; return pose; }
        private void Run(float seconds, BoxerPose pose) { game.input.SetTestPose(pose); for (int i = 0; i < Mathf.CeilToInt(seconds * 100); i++) game.Simulate(.01f, pose); }

        [Test] public void BeforeTheFirstStartTheBoardAsksForTheHandsAndNothingStartsUntilTheyAreSeen()
        {
            // Standing still with the hands down: the startup recenter happens (head only), the tutorial waits.
            var lost = NoHands(BoxingSceneTests.Pose());
            Run(4, lost);
            Assert.That(game.input.StartupRecentered, Is.True, "The startup recenter needs the head only.");
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready));
            Assert.That(game.HandsMissing, Is.True);
            Assert.That(Board, Is.EqualTo(BoxingGame.HandsMissingText));
            Assert.That(game.presentation.timerText.font.name, Does.Contain("Noto"));
            // Hands seen: the tutorial follows.
            Run(game.tuning.autoStartSeconds + 1, BoxingSceneTests.Pose());
            Assert.That(game.HandsMissing, Is.False);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Assert.That(Board, Is.Not.EqualTo(BoxingGame.HandsMissingText));
        }

        [Test] public void FarFromTheStartBeforeTheStartupRecenterTheHandsWarningShows()
        {
            var far = NoHands(BoxingSceneTests.Pose()); far.head.x = 2; far.head.z = 2;
            Run(.3f, far);
            Assert.That(game.input.StartupRecentered, Is.False);
            Assert.That(game.HandsMissing, Is.True);
            Assert.That(Board, Is.EqualTo(BoxingGame.HandsMissingText));
        }

        [Test] public void InTheTutorialAndPastTheGraceInTheRoundTheBoardAsksForTheHands()
        {
            Run(game.tuning.autoStartSeconds + 1, BoxingSceneTests.Pose());
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            var lost = NoHands(BoxingSceneTests.Pose());
            Run(game.tuning.trackingGraceSeconds - .5f, lost);
            Assert.That(game.HandsMissing, Is.False, "A short dropout keeps going.");
            Run(1, lost);
            Assert.That(game.HandsMissing, Is.True); Assert.That(Board, Is.EqualTo(BoxingGame.HandsMissingText));
            Run(.5f, BoxingSceneTests.Pose());
            Assert.That(game.HandsMissing, Is.False); Assert.That(Board, Is.EqualTo(game.TutorialText));

            game.StartRound(); Run(3.5f, BoxingSceneTests.Pose());
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            Run(game.tuning.trackingGraceSeconds + .5f, lost);
            Assert.That(game.HandsMissing, Is.True); Assert.That(Board, Is.EqualTo(BoxingGame.HandsMissingText));
            float left = game.Round.TimeLeft; Run(1, lost);
            Assert.That(game.Round.TimeLeft, Is.EqualTo(left), "The round waits.");
            // The controllers' wording in the controller mode.
            game.input.SelectMode(BoxingInputMode.Controllers); Run(.1f, lost);
            Assert.That(Board, Is.EqualTo(BoxingGame.ControllersMissingText));
        }

        [Test] public void TheWarningsFitTheBoard()
        {
            Run(.3f, NoHands(BoxingSceneTests.Pose()));
            var text = game.presentation.timerText; var box = text.rectTransform.rect;
            foreach (var value in new[] { BoxingGame.HandsMissingText, BoxingGame.ControllersMissingText })
            {
                foreach (var c in value) Assert.That(text.font.HasCharacter(c), Is.True, c.ToString());
                text.text = value;
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(box.width), value);
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(box.height), value);
            }
        }
    }
}
