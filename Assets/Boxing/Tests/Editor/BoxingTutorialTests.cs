using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hapbeat.Boxing.Tests
{
    // The startup tutorial (BoxingGame.StartTutorial) driven with test poses: silent, no Hapbeat output.
    public sealed class BoxingTutorialTests
    {
        private BoxingGame game;
        private BoxingTuning original;
        // Tutorial lines in the order they were spoken (silently: forceSilent), and the time since the first frame.
        private readonly List<AudioClip> spoken = new List<AudioClip>();
        private readonly List<float> spokenAt = new List<float>();
        private float clock;
        private int cues;

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose());
            spoken.Clear(); spokenAt.Clear(); clock = 0; cues = game.feedback.TutorialVoiceCues;
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        private string Board => game.presentation.timerText.text;
        private void Step(BoxerPose pose)
        {
            game.Simulate(.01f, pose); clock += .01f;
            for (; cues < game.feedback.TutorialVoiceCues; cues++) { spoken.Add(game.feedback.LastTutorialVoice); spokenAt.Add(clock); }
        }
        private void Run(float seconds, BoxerPose pose) { for (int i = 0; i < Mathf.CeilToInt(seconds * 100); i++) Step(pose); }
        private bool RunUntil(Func<bool> done, float seconds, BoxerPose pose)
        {
            for (int i = 0; i < Mathf.CeilToInt(seconds * 100) && !done(); i++) Step(pose);
            return done();
        }
        private BoxingFeedback Voice => game.feedback;
        private LineRenderer Marker => game.presentation.TutorialMarker;
        private bool MarkerShown => Marker != null && Marker.gameObject.activeSelf;
        private void Begin()
        {
            Assert.That(RunUntil(() => game.Round.Phase == BoxingPhase.Tutorial, game.tuning.autoStartSeconds + 1, BoxingSceneTests.Pose()), Is.True);
            Assert.That(game.TutorialStep, Is.EqualTo(1));
        }
        // The left glove sets off .45 m short of the target and drives through it (a closed, fast straight).
        private void Punch(Vector3 target)
        {
            var pose = BoxingSceneTests.Pose(); Vector3 start = target + Vector3.back * .45f;
            pose.left = start; Run(.2f, pose);
            for (int i = 1; i <= 25; i++) { pose.left = Vector3.Lerp(start, target + Vector3.forward * .05f, i / 25f); Step(pose); }
            for (int i = 1; i <= 25; i++) { pose.left = Vector3.Lerp(target + Vector3.forward * .05f, start, i / 25f); Step(pose); }
        }

        [Test] public void TuningAssetEnablesTheTutorialWithItsTimings()
        {
            Assert.That(original.tutorialOnStart, Is.True);
            Assert.That(original.tutorialStepTimeoutSeconds, Is.EqualTo(20));
            Assert.That(original.tutorialStrikes, Is.EqualTo(3));
            Assert.That(original.tutorialMessageSeconds, Is.EqualTo(1.5f));
        }

        [Test] public void StartupTeachesFourStepsThenCountsDownToTheRoundWithOneGong()
        {
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Ready));
            Run(.5f, BoxingSceneTests.Pose());
            Assert.That(MarkerShown, Is.False, "No marker before the tutorial.");
            Begin();
            Assert.That(game.menu.IsOpen, Is.False);
            Assert.That(game.Opponent.Scripted, Is.True);
            Run(.6f, BoxingSceneTests.Pose());
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialSteps[0]));
            Assert.That(game.Opponent.Guarding && game.Opponent.Guard == BoxingGuard.HeadStraight, Is.True, "Step 1 holds the head guard.");
            Assert.That(game.Opponent.GuardWeight, Is.GreaterThan(.9f));

            // 1: the marker sits before the guarded face; the punch on it lands on the guard: blocked, no damage.
            Assert.That(MarkerShown, Is.True);
            Assert.That(game.TutorialTarget(out var face), Is.True);
            Assert.That(Vector3.Distance(Marker.transform.position, face), Is.LessThan(.002f));
            Assert.That(face.y, Is.EqualTo(game.Opponent.Head.y).Within(.001f)); Assert.That(face.z, Is.LessThan(Mathf.Min(game.Opponent.Left.z, game.Opponent.Right.z)));
            Assert.That(Marker.GetComponent<Collider>(), Is.Null, "The marker takes no part in collisions.");
            Punch(Marker.transform.position);
            Assert.That(game.Round.EnemyBlocks, Is.GreaterThan(0)); Assert.That(game.Round.Hits, Is.Zero);
            Assert.That(game.Round.EnemyHealth, Is.EqualTo(game.Round.MaximumHealth));
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialResults[0]));
            Assert.That(MarkerShown, Is.False, "Hidden during the result message.");
            Assert.That(RunUntil(() => game.TutorialStep == 2, 4, BoxingSceneTests.Pose()), Is.True);
            Run(.6f, BoxingSceneTests.Pose());
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialSteps[1]));
            Assert.That(game.Opponent.Guarding && game.Opponent.Guard == BoxingGuard.HeadStraight, Is.True, "Step 2 keeps the head guard.");

            // 2: the head guard leaves the body open to a straight punch below the elbows (they hang before the upper belly):
            // the belly marker shows that spot, in front of the body surface.
            Assert.That(MarkerShown, Is.True);
            Vector3 belly = Marker.transform.position;
            Assert.That(belly.y, Is.LessThan(game.Opponent.Body.y - .05f));
            float surface = BoxingSceneTests.OpponentFront(game, false, belly.y).z;
            Assert.That(belly.z, Is.LessThan(surface - .015f).And.GreaterThan(surface - .1f), "Just before the belly's surface.");
            int guarded = game.Round.EnemyBlocks;
            Punch(belly);
            Assert.That(game.Round.Hits, Is.EqualTo(1), "A straight to the body must pass the head guard.");
            Assert.That(game.Round.EnemyBlocks, Is.EqualTo(guarded));
            Assert.That(game.Round.EnemyHealth, Is.LessThan(game.Round.MaximumHealth));
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialResults[1]));
            Assert.That(game.Opponent.AttackId, Is.Zero, "No attacks during steps 1-2.");
            Assert.That(RunUntil(() => game.TutorialStep == 3, 4, BoxingSceneTests.Pose()), Is.True);
            Run(.1f, BoxingSceneTests.Pose());
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialSteps[2]));
            Assert.That(game.Opponent.Guarding, Is.False, "Steps 3-4 drop the guard.");
            Assert.That(MarkerShown, Is.False, "No marker for the strike steps.");

            // 3: the open player takes one straight.
            Assert.That(RunUntil(() => game.Round.Taken > 0, 6, BoxingSceneTests.Pose()), Is.True);
            Assert.That(game.Opponent.Attack, Is.EqualTo(BoxingAttack.HeadStraight));
            Assert.That(RunUntil(() => Board == BoxingGame.TutorialResults[2], 2, BoxingSceneTests.Pose()), Is.True);
            Assert.That(game.Opponent.AttackId, Is.EqualTo(1));
            Assert.That(RunUntil(() => game.TutorialStep == 4, 4, BoxingSceneTests.Pose()), Is.True);
            Run(.1f, BoxingSceneTests.Pose(true));
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialSteps[3]));

            // 4: gloves before the face block the slower straight; then 試合開始 and the countdown.
            int taken = game.Round.Taken;
            Assert.That(RunUntil(() => game.Round.Blocks > 0, 7, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(game.Round.Taken, Is.EqualTo(taken));
            Assert.That(RunUntil(() => Board == BoxingGame.TutorialResults[3], 2, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(RunUntil(() => Board == BoxingGame.TutorialStartText, 4, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Assert.That(game.feedback.Rings, Is.Zero, "No gong for the tutorial.");
            Assert.That(RunUntil(() => game.Round.Phase == BoxingPhase.Countdown, 4, BoxingSceneTests.Pose()), Is.True);
            Assert.That(game.Opponent.Scripted, Is.False, "The round's AI is back.");
            // Each step's instruction and success line once, in order, then 試合開始; the countdown waits for it.
            Assert.That(spoken, Is.EqualTo(new[] { Voice.tutorialStepVoice[0], Voice.tutorialResultVoice[0], Voice.tutorialStepVoice[1], Voice.tutorialResultVoice[1],
                Voice.tutorialStepVoice[2], Voice.tutorialResultVoice[2], Voice.tutorialStepVoice[3], Voice.tutorialResultVoice[3], Voice.tutorialStartVoice }));
            Assert.That(clock - spokenAt[spokenAt.Count - 1], Is.GreaterThanOrEqualTo(Voice.tutorialStartVoice.length), "The countdown starts after 試合開始.");
            Assert.That(game.feedback.voiceSource.isPlaying, Is.False, "Nothing is audible in the tests.");
            Assert.That(game.Round.Taken + game.Round.Hits + game.Round.Blocks + game.Round.EnemyBlocks, Is.Zero, "The round starts from zero.");
            Assert.That(game.Round.PlayerHealth, Is.EqualTo(game.Round.MaximumHealth));
            Assert.That(game.Round.EnemyHealth, Is.EqualTo(game.Round.MaximumHealth));
            Run(3.5f, BoxingSceneTests.Pose());
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            Assert.That(game.feedback.Rings, Is.EqualTo(1), "One gong starts the round.");
            Assert.That(MarkerShown, Is.False, "No marker in the round.");
            Assert.That(game.feedback.Sends, Is.Zero);
        }

        [Test] public void PunchStepsAdvanceAfterTheTimeoutAndAPauseHoldsThem()
        {
            Begin();
            var pose = BoxingSceneTests.Pose();
            game.InputSuspended = true;
            Run(game.tuning.tutorialStepTimeoutSeconds + 5, pose);
            Assert.That(game.TutorialStep, Is.EqualTo(1), "A pause holds the tutorial.");
            game.InputSuspended = false;
            Run(game.tuning.tutorialStepTimeoutSeconds - 1, pose);
            Assert.That(game.TutorialStep, Is.EqualTo(1));
            Assert.That(game.Opponent.AttackId, Is.Zero, "The guarding opponent never attacks.");
            Assert.That(game.Opponent.Evade, Is.EqualTo(BoxingEvade.None));
            bool result = false;
            Assert.That(RunUntil(() => { result |= Board == BoxingGame.TutorialResults[0]; return game.TutorialStep == 2; }, 1.5f, pose), Is.True, "Step 1 times out.");
            Assert.That(result, Is.False, "A timeout skips the success message.");
            Run(game.tuning.tutorialStepTimeoutSeconds - 1, pose);
            Assert.That(game.TutorialStep, Is.EqualTo(2));
            Assert.That(RunUntil(() => { result |= Board == BoxingGame.TutorialResults[1]; return game.TutorialStep == 3; }, 1.5f, pose), Is.True, "Step 2 times out as well.");
            Assert.That(result, Is.False);
            Assert.That(game.Round.Taken + game.Round.Hits, Is.Zero);
        }

        [Test] public void StrikeStepsRepeatOnTheWrongOutcomeAndAdvanceAfterTheirStrikes()
        {
            game.tuning.tutorialStepTimeoutSeconds = 1;
            Begin();
            Assert.That(RunUntil(() => game.TutorialStep == 3, 6, BoxingSceneTests.Pose()), Is.True);

            // 3 asks to take the punch: blocking repeats it with the hint, three strikes move on anyway.
            var guard = BoxingSceneTests.Pose(true);
            bool hint = false, result = false;
            Assert.That(RunUntil(() => { hint |= Board == BoxingGame.TutorialHints[0]; result |= Board == BoxingGame.TutorialResults[2]; return game.TutorialStep == 4; }, 15, guard), Is.True);
            Assert.That(hint, Is.True, "A block shows the hint.");
            Assert.That(result, Is.False, "The last strike moves on without the success message.");
            Assert.That(game.Round.Blocks, Is.EqualTo(game.tuning.tutorialStrikes)); Assert.That(game.Round.Taken, Is.Zero);
            Assert.That(game.Opponent.AttackId, Is.EqualTo(game.tuning.tutorialStrikes));

            // 4 asks to block: a hit repeats it with the hint, three strikes start the round anyway.
            var open = BoxingSceneTests.Pose();
            hint = false; bool start = false;
            Assert.That(RunUntil(() => { hint |= Board == BoxingGame.TutorialHints[1]; result |= Board == BoxingGame.TutorialResults[3]; start |= Board == BoxingGame.TutorialStartText;
                return game.Round.Phase == BoxingPhase.Countdown; }, 20, open), Is.True);
            Assert.That(hint, Is.True, "A hit shows the hint.");
            Assert.That(result, Is.False, "No ガード成功！ without a block.");
            Assert.That(start, Is.True, "試合開始 still announces the round.");
            // Steps speak their instruction once, the retries their hint, and no success line without a success.
            foreach (var clip in new[] { Voice.tutorialStepVoice[2], Voice.tutorialStepVoice[3], Voice.tutorialStartVoice })
                Assert.That(spoken.FindAll(c => c == clip).Count, Is.EqualTo(1), clip.name);
            Assert.That(spoken, Does.Contain(Voice.tutorialHintVoice[0])); Assert.That(spoken, Does.Contain(Voice.tutorialHintVoice[1]));
            Assert.That(spoken, Has.No.Member(Voice.tutorialResultVoice[2])); Assert.That(spoken, Has.No.Member(Voice.tutorialResultVoice[3]));
            Assert.That(game.Opponent.AttackId, Is.Zero, "StartRound resets the opponent.");
        }

        [Test] public void TutorialVoiceClipsAreAssignedLoadedAndShort()
        {
            Assert.That(Voice.voiceSource, Is.Not.Null);
            var clips = new List<AudioClip>(Voice.tutorialStepVoice); clips.AddRange(Voice.tutorialResultVoice); clips.AddRange(Voice.tutorialHintVoice); clips.Add(Voice.tutorialStartVoice);
            Assert.That(clips.Count, Is.EqualTo(11));
            foreach (var clip in clips)
            {
                Assert.That(clip, Is.Not.Null);
                Assert.That(clip.LoadAudioData(), Is.True, clip.name); Assert.That(clip.samples, Is.GreaterThan(0), clip.name);
                Assert.That(clip.length, Is.InRange(.3f, 4f), clip.name);
            }
            Assert.That(new HashSet<AudioClip>(clips).Count, Is.EqualTo(clips.Count), "One clip per line.");
            Assert.That(Voice.tutorialStepVoice[0].name, Is.EqualTo("step1")); Assert.That(Voice.tutorialResultVoice[3].name, Is.EqualTo("step4_ok"));
            Assert.That(Voice.tutorialHintVoice[1].name, Is.EqualTo("step4_retry")); Assert.That(Voice.tutorialStartVoice.name, Is.EqualTo("start"));
        }

        [Test] public void StrikesWaitForTheLineAndMessagesStayUntilTheirLineEnds()
        {
            game.tuning.tutorialStepTimeoutSeconds = 1;
            Begin();
            Assert.That(spoken, Is.EqualTo(new[] { Voice.tutorialStepVoice[0] }), "Step 1 speaks its line when it appears.");
            Assert.That(RunUntil(() => game.TutorialStep == 3, 6, BoxingSceneTests.Pose()), Is.True);
            Assert.That(spoken[spoken.Count - 1], Is.SameAs(Voice.tutorialStepVoice[2]));
            float instruction = spokenAt[spokenAt.Count - 1];
            Assert.That(RunUntil(() => game.Opponent.AttackId > 0, 6, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(clock - instruction, Is.GreaterThanOrEqualTo(Voice.tutorialStepVoice[2].length), "The first strike waits for the instruction.");
            // A block brings the retry line: its message stays until the line ends, and the next strike waits for it.
            Assert.That(RunUntil(() => spoken[spoken.Count - 1] == Voice.tutorialHintVoice[0], 6, BoxingSceneTests.Pose(true)), Is.True);
            float hint = spokenAt[spokenAt.Count - 1];
            Assert.That(Board, Is.EqualTo(BoxingGame.TutorialHints[0]));
            Assert.That(RunUntil(() => Board != BoxingGame.TutorialHints[0], 6, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(clock - hint, Is.GreaterThanOrEqualTo(Mathf.Max(game.tuning.tutorialMessageSeconds, Voice.tutorialHintVoice[0].length + .2f) - .02f));
            Assert.That(RunUntil(() => game.Opponent.AttackId > 1, 6, BoxingSceneTests.Pose(true)), Is.True);
            Assert.That(clock - hint, Is.GreaterThanOrEqualTo(Voice.tutorialHintVoice[0].length), "The repeated strike waits for the retry line.");
            Assert.That(game.feedback.Sends, Is.Zero);
        }

        [Test] public void StartRoundSkipsTheTutorialAndRetryOrRestartNeverReplaysIt()
        {
            Begin();
            game.StartRound();
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            Assert.That(game.TutorialStep, Is.Zero);
            Assert.That(game.Opponent.Scripted, Is.False);
            Assert.That(game.Opponent.Guarding, Is.False, "The held tutorial guard is dropped.");
            var pose = BoxingSceneTests.Pose();
            Run(8, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            Assert.That(game.Opponent.Actions, Is.GreaterThan(0), "The AI chooses its own actions again.");
            // The round ends and the next one starts by itself: straight into the countdown.
            game.Round.Tick(1000, false); game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(RunUntil(() => game.Round.Phase != BoxingPhase.Results, game.RestartTime + 1, pose), Is.True);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            game.StartRound();
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown), "Restart / retry is the plain round.");
        }

        [Test] public void WithoutTheTutorialTheStartupCountsDownAsBefore()
        {
            game.tuning.tutorialOnStart = false;
            var pose = BoxingSceneTests.Pose();
            Assert.That(RunUntil(() => game.Round.Phase != BoxingPhase.Ready, game.tuning.autoStartSeconds + 1, pose), Is.True);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            Assert.That(game.Opponent.Scripted, Is.False);
        }

        [Test] public void JapaneseBoardTextUsesTheBundledFontAndFitsTheBoard()
        {
            var text = game.presentation.timerText;
            var latin = text.font; int size = text.fontSize;
            Begin(); Run(.3f, BoxingSceneTests.Pose());
            Assert.That(text.font.name, Does.Contain("Noto"));
            Assert.That(text.fontSize, Is.LessThan(size));
            var all = string.Join("", BoxingGame.TutorialSteps) + string.Join("", BoxingGame.TutorialResults) + string.Join("", BoxingGame.TutorialHints) + BoxingGame.TutorialStartText;
            foreach (var character in all)
                if (character != ' ') Assert.That(text.font.HasCharacter(character), Is.True, character.ToString());
            var box = text.rectTransform.rect;
            foreach (var line in new[] { BoxingGame.TutorialSteps, BoxingGame.TutorialResults, BoxingGame.TutorialHints, new[] { BoxingGame.TutorialStartText } })
            foreach (var value in line)
            {
                text.text = value;
                Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(box.width), value);
                Assert.That(text.preferredHeight, Is.LessThanOrEqualTo(box.height), value);
            }
            game.StartRound(); Run(.3f, BoxingSceneTests.Pose());
            Assert.That(text.font, Is.SameAs(latin), "The round's board text is unchanged.");
            Assert.That(text.fontSize, Is.EqualTo(size));
        }
    }
}
