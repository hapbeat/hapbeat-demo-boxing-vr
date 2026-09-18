using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingForearmTests
    {
        private BoxingGame game;
        private BoxingTuning original;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            game=Object.FindFirstObjectByType<BoxingGame>();
            original=game.tuning; game.tuning=Object.Instantiate(original); game.tuning.maximumHealth=10000;
            game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false); game.Initialize();
            game.input.SetTestPose(BoxingSceneTests.Pose()); game.StartRound();
        }
        [TearDown] public void Cleanup()
        { Object.DestroyImmediate(game.tuning); game.tuning=original; EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        [Test] public void SweepHitsForearmButNotGloveAndRejectsOutside()
        {
            Vector3 hand=Vector3.zero, a=new Vector3(-.3f,0,-.24f), b=new Vector3(.3f,0,-.24f);
            Assert.That(BoxingCollision.Sweep(a,b,.07f,hand,hand,.07f,out _),Is.False);
            Assert.That(BoxingForearm.Sweep(a,b,.07f,hand,Quaternion.identity,hand,Quaternion.identity,out float t,out _,out _),Is.True);
            Assert.That(t,Is.InRange(0,1));
            Assert.That(BoxingForearm.Sweep(a+Vector3.up*.2f,b+Vector3.up*.2f,.07f,hand,Quaternion.identity,hand,Quaternion.identity,out _,out _,out _),Is.False);
        }
        [Test] public void MovingForearmSweepsThroughStationaryPunch()
        {
            var point=Vector3.back*.24f;
            Assert.That(BoxingForearm.Sweep(point,point,.07f,Vector3.left*.3f,Quaternion.identity,Vector3.right*.3f,Quaternion.identity,out _,out _,out _),Is.True);
        }
        [Test] public void RaisedGlovesLeaveForearmsToBlockOnBothExistingWristChannels()
        {
            var pose=BoxingSceneTests.Pose(true); pose.left.y=pose.right.y=2.0f;
            pose.leftRotation=pose.rightRotation=Quaternion.LookRotation(Vector3.up,Vector3.back);
            int left=0,right=0;
            game.feedback.Reported+=impact=> {
                if(impact.attack || impact.surface!=ImpactSurface.Glove) return;
                Assert.That(impact.point.y,Is.LessThan(1.85f),"The raised glove itself must not make this guard.");
                if(impact.zone==ImpactZone.LeftGlove) left++; else if(impact.zone==ImpactZone.RightGlove) right++;
            };
            for(int i=0;i<8500;i++) game.Simulate(.01f,pose);
            Assert.That(left,Is.GreaterThan(0)); Assert.That(right,Is.GreaterThan(0));
            Assert.That(game.Round.Blocks+game.Round.Taken,Is.LessThanOrEqualTo(game.Opponent.AttackId));
            Assert.That(game.feedback.Sends,Is.Zero);
        }
        [Test] public void VisualForearmsFollowWristAndHideWithMenuOrLostTracking()
        {
            Assert.That(game.presentation.cueText.transform.Find("YOU HP"),Is.Null,"Runtime health bars must not be saved into the scene.");
            var pose=BoxingSceneTests.Pose(); pose.leftRotation=Quaternion.Euler(30,60,90);
            game.presentation.Render(game,pose,true);
            var arm=game.presentation.leftForearm;
            Assert.That(arm,Is.Not.Null); Assert.That(game.presentation.rightForearm,Is.Not.Null);
            var size=arm.GetComponent<MeshFilter>().sharedMesh.bounds.size;
            Assert.That(size.y,Is.EqualTo(BoxingForearm.Length).Within(.001f));
            Assert.That(size.x,Is.EqualTo(BoxingForearm.Radius*2).Within(.001f));
            Assert.That(Vector3.Distance(arm.position,pose.left+pose.leftRotation*Vector3.back*.24f),Is.LessThan(.001f));
            game.menu.Open(); game.presentation.Render(game,pose,false); Assert.That(arm.gameObject.activeSelf,Is.False);
            game.menu.Close(); pose.valid=pose.visualValid=false; game.presentation.Render(game,pose,false);
            Assert.That(arm.gameObject.activeSelf,Is.False);
        }
        [Test] public void LostTrackingCannotGuardWithForearms()
        {
            var pose=BoxingSceneTests.Pose(true); pose.left.y=pose.right.y=2;
            pose.leftRotation=pose.rightRotation=Quaternion.LookRotation(Vector3.up,Vector3.back);
            pose.valid=false; pose.visualValid=true;
            for(int i=0;i<1000;i++) game.Simulate(.01f,pose);
            Assert.That(game.Round.Blocks,Is.Zero); Assert.That(game.feedback.Reports,Is.Zero);
        }
        [TestCase(BoxingOutcome.Win)] [TestCase(BoxingOutcome.Lose)] [TestCase(BoxingOutcome.Tie)]
        public void OutcomeVoiceFollowsGongOnceAndMenuWaits(BoxingOutcome outcome)
        {
            var pose=BoxingSceneTests.Pose();
            for(int i=0;i<350;i++) game.Simulate(.01f,pose);
            if(outcome!=BoxingOutcome.Tie) game.Round.Report(new BoxingImpact(
                outcome==BoxingOutcome.Win ? ImpactZone.LeftGlove : ImpactZone.Head,1,outcome==BoxingOutcome.Win,Vector3.zero,game.tuning,ImpactSurface.Body));
            game.Round.Tick(90,false); game.Simulate(.01f,pose);
            Assert.That(game.Round.Outcome,Is.EqualTo(outcome)); Assert.That(game.feedback.ResultClip(outcome),Is.Not.Null);
            int rings=game.feedback.Rings;
            while(game.ResultPresentationTime<game.ResultDuration-.05f)
            {
                game.Simulate(.01f,pose);
                if(game.feedback.ResultVoiceCues>0) Assert.That(game.feedback.Rings,Is.EqualTo(rings+1));
                Assert.That(game.menu.IsOpen,Is.False);
            }
            for(int i=0;i<30;i++) game.Simulate(.01f,pose);
            Assert.That(game.menu.IsOpen,Is.True); Assert.That(game.feedback.ResultVoiceCues,Is.EqualTo(1));
            Assert.That(game.feedback.LastSpokenOutcome,Is.EqualTo(outcome));
        }
    }
}
