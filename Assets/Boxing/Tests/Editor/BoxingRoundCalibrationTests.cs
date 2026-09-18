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
                Assert.That(game.Opponent.Head.y,Is.EqualTo(2.1f).Within(.001f));
                Assert.That(game.Opponent.Root.y,Is.EqualTo(.4f).Within(.001f));
                Assert.That(game.presentation.arena.position.y,Is.EqualTo(.4f).Within(.001f));
                Assert.That(game.presentation.timerText.transform.parent.position.y,Is.EqualTo(3.15f).Within(.001f));
            }
        }
        [Test] public void StartRoundRecapturesHeightEvenBeforeBothHandsAreReady()
        {
            foreach(float height in new[]{1.3f,1.85f,1.5f})
            {
                game.input.headCamera.transform.position=new Vector3(0,height,.2f);
                game.StartRound();
                Assert.That(game.Opponent.Head.y,Is.EqualTo(height).Within(.001f));
            }
        }
    }
}
