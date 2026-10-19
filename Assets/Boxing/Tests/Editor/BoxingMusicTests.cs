using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    // The round music (BoxingFeedback.StartMusic / SyncMusic / StopMusic) driven silently: forceSilent, no Hapbeat output.
    public sealed class BoxingMusicTests
    {
        private BoxingGame game;
        private BoxingTuning original;

        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game = Object.FindFirstObjectByType<BoxingGame>();
            original = game.tuning; game.tuning = Object.Instantiate(original);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose());
        }

        [TearDown] public void Cleanup()
        {
            Object.DestroyImmediate(game.tuning); game.tuning = original;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }

        private BoxingFeedback Feedback => game.feedback;
        private void Run(float seconds, BoxerPose pose) { for (int i = 0; i < Mathf.CeilToInt(seconds * 100); i++) game.Simulate(.01f, pose); }

        [Test] public void MusicIsAStreamedVorbisClipOnItsOwn2DSourceUnderTheImpacts()
        {
            Assert.That(Feedback.musicSource, Is.Not.Null);
            Assert.That(Feedback.musicSource, Is.Not.SameAs(Feedback.voiceSource));
            Assert.That(Feedback.musicSource, Is.Not.SameAs(Feedback.bellSource));
            Assert.That(Feedback.musicSource.spatialBlend, Is.Zero);
            Assert.That(Feedback.musicSource.playOnAwake, Is.False);
            Assert.That(Feedback.music, Is.Not.Null);
            Assert.That(Feedback.music.length, Is.EqualTo(55).Within(.1f));
            var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Feedback.music));
            Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
            Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis));
            // About -18 dB, well under the impact sounds (played at up to the full impact gain).
            Assert.That(20 * Mathf.Log10(original.musicVolume), Is.EqualTo(-18).Within(1));
            Assert.That(original.roundSeconds, Is.EqualTo(55), "One default round lasts the music.");
        }

        [Test] public void MusicStartsAtTheGongPausesWithTheRoundAndStopsAtTheFinalBell()
        {
            var pose = BoxingSceneTests.Pose();
            game.StartRound();
            Run(1, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Countdown));
            Assert.That(Feedback.MusicStarts, Is.Zero, "No music during the countdown.");
            Run(2.5f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            Assert.That(Feedback.MusicStarts, Is.EqualTo(1)); Assert.That(Feedback.MusicPlaying, Is.True);
            Assert.That(Feedback.musicSource.loop, Is.False, "A 55 s round plays the 55 s clip once.");

            // The menu, the shared pause and a tracking loss hold the music with the round; it resumes in place.
            game.menu.Open(); Run(.1f, pose);
            Assert.That(Feedback.MusicPaused, Is.True);
            // Each resume first holds still for TrackingReady (0.2 s), music and clock together.
            game.menu.Close(); Run(.3f, pose);
            Assert.That(Feedback.MusicPlaying, Is.True);
            game.InputSuspended = true; float left = game.Round.TimeLeft; Run(.5f, pose);
            Assert.That(Feedback.MusicPaused, Is.True); Assert.That(game.Round.TimeLeft, Is.EqualTo(left), "The round clock holds as well.");
            game.InputSuspended = false; Run(.3f, pose);
            Assert.That(Feedback.MusicPlaying, Is.True);
            var lost = pose; lost.valid = false; Run(game.tuning.trackingGraceSeconds + .5f, lost);
            Assert.That(Feedback.MusicPaused, Is.True);
            Run(.5f, pose);
            Assert.That(Feedback.MusicPlaying, Is.True);
            Assert.That(Feedback.MusicStarts, Is.EqualTo(1), "Resuming is not a new start.");
            Assert.That(Feedback.MusicStops, Is.Zero);

            game.Round.Tick(1000, false); game.Simulate(.01f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(Feedback.MusicStops, Is.EqualTo(1)); Assert.That(Feedback.MusicPlaying, Is.False);
            Assert.That(Feedback.musicSource.isPlaying, Is.False, "Nothing is audible in the tests.");
            Assert.That(Feedback.Sends, Is.Zero);
        }

        [Test] public void AThirtySecondRoundFadesTheMusicAtTheFinalBell()
        {
            game.tuning.roundSeconds = 30;
            var pose = BoxingSceneTests.Pose();
            game.StartRound();
            Run(3.5f, pose);
            Assert.That(Feedback.MusicStarts, Is.EqualTo(1));
            Assert.That(Feedback.musicSource.loop, Is.False);
            for (int i = 0; i < 3200 && game.Round.Phase == BoxingPhase.Fighting; i++)
            {
                game.Simulate(.01f, pose);
                if (game.Round.Phase == BoxingPhase.Fighting) Assert.That(Feedback.MusicStops, Is.Zero);
            }
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(Feedback.MusicStops, Is.EqualTo(1), "Stopped on the final bell's frame.");
            Assert.That(Feedback.MusicStarts, Is.EqualTo(1));
        }

        [Test] public void NoMusicInTheTutorialAndRestartsStopIt()
        {
            var pose = BoxingSceneTests.Pose();
            Run(game.tuning.autoStartSeconds + .5f, pose);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Tutorial));
            Run(3, pose);
            Assert.That(Feedback.MusicStarts, Is.Zero, "No music during the tutorial.");
            game.StartRound(); Run(3.5f, pose);
            Assert.That(Feedback.MusicPlaying, Is.True);
            game.StartRound();
            Assert.That(Feedback.MusicStops, Is.EqualTo(1), "StartRound fades the music out.");
            Assert.That(Feedback.MusicPlaying, Is.False);
            Run(3.5f, pose);
            Assert.That(Feedback.MusicStarts, Is.EqualTo(2), "The next gong starts it again.");
            game.StartTutorial();
            Assert.That(Feedback.MusicStops, Is.EqualTo(2), "The tutorial start fades it out as well.");
            Run(2, pose);
            Assert.That(Feedback.MusicStarts, Is.EqualTo(2));
        }
    }
}
