using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingPolishTests
    {
        private BoxingGame game;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game=Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false); game.menu.Close();
        }
        [TearDown] public void Cleanup()=>EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        [TestCase(1.4f)] [TestCase(1.85f)] public void RecenterCalibratesOpponentButDuckingDoesNotResizeIt(float height)
        {
            game.input.headCamera.transform.position=new Vector3(0,height,0);
            game.RecenterPlayer();
            Assert.That(game.Opponent.Head.y,Is.EqualTo(height).Within(.001f));
            game.input.headCamera.transform.position=Vector3.up*(height-.3f);
            game.Opponent.Tick(.01f,game.input.headCamera.transform.position,false);
            Assert.That(game.Opponent.Head.y,Is.EqualTo(height).Within(.002f));
        }
        [Test] public void OpponentUsesHeadAndBodyGuards()
        {
            bool head=false,body=false;
            for(int i=0;i<9000;i++)
            {
                game.Opponent.Tick(.01f,Vector3.up*1.65f,true);
                if(game.Opponent.GuardWeight<.95f) continue;
                if(game.Opponent.BodyGuard) {body=true; Assert.That(game.Opponent.Left.y,Is.LessThan(game.Opponent.Head.y-.3f));}
                else {head=true; Assert.That(game.Opponent.Left.y,Is.GreaterThan(game.Opponent.Head.y-.1f));}
            }
            Assert.That(head && body,Is.True);
        }
        [Test] public void ResultRemainsVisibleBeforeMenuAppears()
        {
            var pose=BoxingSceneTests.Pose(); game.input.SetTestPose(pose); game.StartRound();
            game.Round.Tick(3,false);
            for(int i=0;i<30;i++) game.Simulate(.01f,pose);
            game.Round.Tick(90,false); game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.False);
            Assert.That(game.presentation.timerText.text,Does.Contain("ROUND COMPLETE"));
            for(int i=0;i<240;i++) game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.False);
            for(int i=0;i<20;i++) game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.True); Assert.That(game.feedback.Rings,Is.EqualTo(2));
        }
        [Test] public void GlovesShareWidthRegardlessOfOpponentHeight()
        {
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.leftGlove).x,Is.EqualTo(.16f).Within(.001f));
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.rightGlove).x,Is.EqualTo(.16f).Within(.001f));
            var avatar=game.presentation.enemyAvatar;
            foreach(float h in new[]{1.3f,1.9f})
            {
                game.Opponent.Reset(h); avatar.Render(game.Opponent,0);
                Assert.That(avatar.leftGloveLocalWidth*avatar.leftHand.lossyScale.x,Is.EqualTo(.16f).Within(.001f));
            }
        }
        [Test] public void MissingHandPredictionIsShortAndBounded()
        {
            var visual=new BoxingHandVisual(); Vector3 p=Vector3.zero; Quaternion r=Quaternion.identity;
            visual.Sample(true,ref p,ref r,0); p=Vector3.forward*.1f; visual.Sample(true,ref p,ref r,.01);
            Assert.That(visual.Sample(false,ref p,ref r,.10),Is.True);
            Assert.That(p.z,Is.LessThanOrEqualTo(.151f));
            Assert.That(visual.Sample(false,ref p,ref r,.14),Is.False);
        }
    }
}
