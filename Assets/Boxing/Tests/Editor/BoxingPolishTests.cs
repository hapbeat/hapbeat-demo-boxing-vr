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
            Assert.That(game.feedback.ResultVoiceCues,Is.LessThanOrEqualTo(1));
            for(int i=0;i<Mathf.CeilToInt(game.ResultDuration*100);i++) game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.True); Assert.That(game.feedback.Rings,Is.EqualTo(2));
            Assert.That(game.feedback.ResultVoiceCues,Is.EqualTo(1));
        }
        [Test] public void GloveWidthsRemainFixedRegardlessOfOpponentHeight()
        {
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.leftGlove).x,Is.EqualTo(.15f).Within(.001f));
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.rightGlove).x,Is.EqualTo(.15f).Within(.001f));
            var avatar=game.presentation.enemyAvatar;
            foreach(float h in new[]{1.3f,1.9f})
            {
                game.Opponent.Reset(h); avatar.Render(game.Opponent,0);
                Assert.That(avatar.leftGloveLocalWidth*avatar.leftHand.lossyScale.x,Is.EqualTo(.18f).Within(.001f));
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
        [Test] public void PlayerGloveAnatomyMatchesTrackedWristFrame()
        {
            foreach(var pair in new[]{(game.presentation.leftGlove,1f),(game.presentation.rightGlove,-1f)})
            {
                var mesh=pair.Item1.GetComponentInChildren<MeshFilter>().sharedMesh;
                Vector3 thumb=default,palm=default,cuff=default; int nt=0,np=0,nc=0;
                var colors=mesh.colors; var vertices=mesh.vertices;
                Assert.That(colors.Length,Is.EqualTo(vertices.Length));
                for(int i=0;i<vertices.Length;i++)
                {
                    if(colors[i].r>.9f) {thumb+=vertices[i]; nt++;}
                    if(colors[i].g>.9f) {palm+=vertices[i]; np++;}
                    if(colors[i].b>.9f) {cuff+=vertices[i]; nc++;}
                }
                Assert.That(nt,Is.GreaterThan(0)); Assert.That(np,Is.GreaterThan(0)); Assert.That(nc,Is.GreaterThan(0));
                Assert.That((thumb/nt).x*pair.Item2,Is.GreaterThan(.02f),"Thumb must point inward, not outside the hand.");
                Assert.That((palm/np).y,Is.LessThan(-.015f),"Folded fingers belong on the palm, not the dorsum.");
                Assert.That((cuff/nc).z,Is.LessThan(-.04f),"Cuff must sit behind the knuckles.");
            }
        }
        [Test] public void CountdownVoiceAssetsAndGhostHandsAreInstalled()
        {
            Assert.That(game.feedback.voiceSource,Is.Not.Null);
            foreach(var clip in game.feedback.countdownVoice) { Assert.That(clip,Is.Not.Null); Assert.That(clip.length,Is.InRange(.1f,1f)); }
            Assert.That(Object.FindObjectsByType<BoxingMenuHand>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length,Is.EqualTo(2));
        }
        [Test] public void CountdownSpeaksThreeTwoOneOnceAndRestarts()
        {
            var pose=BoxingSceneTests.Pose(); game.input.SetTestPose(pose); game.StartRound();
            int cues=game.feedback.VoiceCues;
            for(int i=0;i<340;i++) game.Simulate(.01f,pose);
            Assert.That(game.feedback.VoiceCues-cues,Is.EqualTo(3)); Assert.That(game.feedback.LastSpokenNumber,Is.EqualTo(1));
            game.StartRound(); for(int i=0;i<25;i++) game.Simulate(.01f,pose);
            Assert.That(game.feedback.LastSpokenNumber,Is.EqualTo(3));
        }
    }
}
