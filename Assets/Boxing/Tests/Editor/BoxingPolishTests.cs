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
            // Eye height x the size probe (BoxingTuning.opponentSizeScale).
            float head=height*game.tuning.opponentSizeScale;
            Assert.That(game.Opponent.Head.y,Is.EqualTo(head).Within(.001f));
            game.input.headCamera.transform.position=Vector3.up*(height-.3f);
            game.Opponent.Tick(.01f,game.input.headCamera.transform.position,Quaternion.identity,false);
            Assert.That(game.Opponent.Head.y,Is.EqualTo(head).Within(.002f));
        }
        [Test] public void OpponentUsesHeadAndBodyGuards()
        {
            bool head=false,body=false;
            for(int i=0;i<9000;i++)
            {
                game.Opponent.Tick(.01f,Vector3.up*1.65f,Quaternion.identity,true);
                if(game.Opponent.GuardWeight<.95f) continue;
                var o=game.Opponent;
                if(o.Guard==BoxingGuard.BodyStraight) {body=true; Assert.That(o.Left.y,Is.LessThan(o.Head.y-.3f));}
                // A body hook guard drops the covering side's elbow (its glove below the chin); the other glove stays at the chin.
                else if(o.Guard==BoxingGuard.BodyHook)
                {
                    body=true; Vector3 cover=o.GuardSide<0 ? o.Left : o.Right, other=o.GuardSide<0 ? o.Right : o.Left;
                    Assert.That(cover.y,Is.LessThan(o.Head.y-.1f)); Assert.That(other.y,Is.GreaterThan(o.Head.y-.1f));
                }
                else {head=true; Assert.That(game.Opponent.Left.y,Is.GreaterThan(game.Opponent.Head.y-.1f));}
            }
            Assert.That(head && body,Is.True);
        }
        [Test] public void ResultRemainsVisibleBeforeTheNextRoundStarts()
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
            Assert.That(game.Round.Phase,Is.EqualTo(BoxingPhase.Results)); Assert.That(game.feedback.Rings,Is.EqualTo(2));
            Assert.That(game.feedback.ResultVoiceCues,Is.EqualTo(1));
            // A pause holds the result; outside a session the next round then starts by itself (no menu).
            game.InputSuspended=true;
            for(int i=0;i<Mathf.CeilToInt(game.RestartTime*100)+50;i++) game.Simulate(.01f,pose);
            Assert.That(game.Round.Phase,Is.EqualTo(BoxingPhase.Results));
            game.InputSuspended=false;
            for(int i=0;i<Mathf.CeilToInt(game.RestartTime*100)+5 && game.Round.Phase==BoxingPhase.Results;i++) game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.False);
            Assert.That(game.Round.Phase,Is.EqualTo(BoxingPhase.Countdown)); Assert.That(game.Round.TimeLeft,Is.EqualTo(game.tuning.roundSeconds));
        }
        [Test] public void GloveWidthsRemainFixedRegardlessOfOpponentHeight()
        {
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.leftGlove).x,Is.EqualTo(.15f).Within(.001f));
            Assert.That(Editor.BoxingContent.GloveSize(game.presentation.rightGlove).x,Is.EqualTo(.15f).Within(.001f));
            var avatar=game.presentation.enemyAvatar;
            foreach(float h in new[]{1.3f,1.9f})
            {
                game.Opponent.Reset(h); avatar.Render(game.Opponent,0);
                Assert.That(avatar.leftGloveLocalWidth*avatar.leftHand.lossyScale.x,Is.EqualTo(.15f).Within(.001f), "The opponent's gloves match the player's width.");
            }
        }
        // Visible size of the opponent's glove (hand bone's weighted vertices at the rendered scale) next to the player's.
        [Test] public void OpponentGloveMatchesThePlayerGloveSize()
        {
            var avatar=game.presentation.enemyAvatar; game.Opponent.Reset(1.65f); avatar.Render(game.Opponent,0);
            var skin=avatar.skin; var mesh=skin.sharedMesh; int index=System.Array.IndexOf(skin.bones,avatar.leftHand);
            var vertices=mesh.vertices; var weights=mesh.boneWeights; var bounds=new Bounds(); bool first=true;
            for(int i=0;i<vertices.Length;i++)
            {
                if(!(weights[i].boneIndex0==index && weights[i].weight0>.9f)) continue;
                var point=Vector3.Scale(mesh.bindposes[index].MultiplyPoint3x4(vertices[i]),avatar.leftHand.lossyScale);
                if(first) { bounds=new Bounds(point,Vector3.zero); first=false; } else bounds.Encapsulate(point);
            }
            var player=Editor.BoxingContent.GloveSize(game.presentation.leftGlove);
            Debug.Log($"BOXING_GLOVE_SIZE opponent={bounds.size.x:F3}x{bounds.size.y:F3}x{bounds.size.z:F3} player={player.x:F3}x{player.y:F3}x{player.z:F3}");
            Assert.That(bounds.size.x,Is.EqualTo(player.x).Within(.002f),"Same width as the player's glove.");
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
