using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingRoundCalibrationTests
    {
        private BoxingGame game;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game=Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false);
        }
        [TearDown] public void Cleanup()=>EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        [Test] public void FloorOffsetCalibratesOpponentArenaAndScoreboardWithoutDrift()
        {
            game.input.origin.transform.position=Vector3.up*.4f;
            game.input.headCamera.transform.position=new Vector3(0,2.1f,.2f);
            for(int i=0;i<3;i++)
            {
                game.StartRound();
                Assert.That(game.input.ReferenceEyeHeight,Is.EqualTo(1.7f).Within(.001f));
                // The game floor is the origin's height at the start (.4); the opponent and the board are set for the forced eye height.
                Assert.That(game.Opponent.Head.y,Is.EqualTo(.4f+BoxingInput.EyeHeight*game.tuning.opponentSizeScale).Within(.001f));
                Assert.That(game.Opponent.Root.y,Is.EqualTo(.4f).Within(.001f));
                Assert.That(game.presentation.arena.position.y,Is.EqualTo(.4f).Within(.001f));
                Assert.That(game.presentation.timerText.transform.parent.position.y,Is.EqualTo(.4f+BoxingInput.EyeHeight+1.05f).Within(.001f));
            }
        }
        [Test] public void StartRoundNeverSizesTheOpponentFromTheHeadHeight()
        {
            foreach(float height in new[]{1.3f,1.85f,1.5f})
            {
                game.input.headCamera.transform.position=new Vector3(0,height,.2f);
                game.StartRound();
                Assert.That(game.input.ReferenceEyeHeight,Is.EqualTo(height).Within(.001f),"Only logged.");
                Assert.That(game.Opponent.Head.y,Is.EqualTo(BoxingInput.EyeHeight*game.tuning.opponentSizeScale).Within(.001f));
            }
        }
        // Size probe (TODO with BoxingTuning.opponentSizeScale): the opponent is sized from eye height x the scale, avatar and
        // hit shapes with it, the gloves stay 15 cm wide, and the status line shows the readings.
        [Test] public void SizeProbeScalesTheWholeOpponentAndShowsTheReadings()
        {
            Assert.That(game.tuning.opponentSizeScale,Is.EqualTo(1),"Back to the original size.");
            var original=game.tuning; game.tuning=Object.Instantiate(original); game.tuning.opponentSizeScale=1.3f; game.Initialize();
            try {
            game.input.headCamera.transform.position=new Vector3(0,1.65f,.2f);
            game.StartRound();
            var avatar=game.presentation.enemyAvatar; avatar.Render(game.Opponent,0);
            Assert.That(game.SizeEyeHeight,Is.EqualTo(1.65f).Within(.001f));
            Assert.That(game.Opponent.Head.y,Is.EqualTo(1.65f*1.3f).Within(.001f));
            Assert.That(avatar.transform.localScale.x,Is.EqualTo(avatar.ScaleFor(game.Opponent)).Within(1e-4f));
            Assert.That(game.SizeOpponentStature,Is.EqualTo(BoxingOpponentAvatar.RestStature*avatar.transform.localScale.x).Within(1e-4f));
            Assert.That(game.SizeOpponentStature,Is.GreaterThan(2.2f),"1.81 m at scale 1, 1.3 times that here.");
            Assert.That(avatar.leftGloveLocalWidth*avatar.leftHand.lossyScale.x,Is.EqualTo(.15f).Within(.001f));
            game.presentation.Render(game,BoxingSceneTests.Pose(),true,.01f);
            StringAssert.Contains("EYE 1.65m (now ",game.presentation.statusText.text);
            StringAssert.Contains("OPP "+game.SizeOpponentStature.ToString("0.00")+"m",game.presentation.statusText.text);
            } finally { Object.DestroyImmediate(game.tuning); game.tuning=original; }
        }
    }
}
