using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingGuardArmTests
    {
        [Test] public void ArmSweepInterceptsFastPunchAndMovingArmButRejectsMiss()
        {
            var arm=new BoxingGuardArm(Vector3.down*.2f,Vector3.up*.2f,.06f);
            Assert.That(BoxingGuardArm.Sweep(Vector3.back,Vector3.forward,.07f,arm,arm,out float t,out _,out _),Is.True);
            Assert.That(t,Is.InRange(0,1));
            Assert.That(BoxingGuardArm.Sweep(Vector3.back+Vector3.right*.3f,Vector3.forward+Vector3.right*.3f,.07f,arm,arm,out _,out _,out _),Is.False);
            var moved=new BoxingGuardArm(arm.start+Vector3.right,arm.end+Vector3.right,.06f);
            Assert.That(BoxingGuardArm.Sweep(Vector3.right*.5f,Vector3.right*.5f,.07f,arm,moved,out _,out _,out _),Is.True);
        }
        [Test] public void BothNpcArmsMatchRigAndBlockWithoutBodyDamageOrRepeatedOverlap()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            try
            {
                var game=Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
                game.feedback.forceSilent=true; game.feedback.sdkRoot.SetActive(false);
                game.StartRound(); game.Round.Tick(3,false);
                var pose=BoxingSceneTests.Pose();
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(BoxingGame).GetMethod("SaveHistory",flags).Invoke(game,new object[]{pose});
                var arms=new BoxingGuardArm[4]; game.presentation.enemyAvatar.GetGuardArms(arms);
                Assert.That(arms[0].start,Is.EqualTo(game.presentation.enemyAvatar.leftUpper.position));
                Assert.That(arms[3].end,Is.EqualTo(game.presentation.enemyAvatar.rightHand.position));
                foreach(var arm in arms) Assert.That(arm.radius,Is.InRange(.025f,.15f));
                int verified=0;
                foreach(int index in new[]{1,3})
                {
                    Vector3 to=(arms[index].start+arms[index].end)*.5f;
                    bool found=false;
                    foreach(var direction in new[]{Vector3.left,Vector3.right,Vector3.back,Vector3.forward,Vector3.up,Vector3.down})
                    {
                        Vector3 from=to+direction*.45f;
                        bool Touch(Vector3 center,float radius)=>BoxingCollision.Sweep(from,to,game.tuning.gloveRadius,center,center,radius,out _);
                        if(Touch(game.Opponent.Left,game.tuning.enemyGloveRadius)||Touch(game.Opponent.Right,game.tuning.enemyGloveRadius)||
                           Touch(game.Opponent.Head,game.tuning.enemyHeadRadius)||Touch(game.Opponent.Body,game.tuning.enemyBodyRadius)) continue;
                        object[] args={to,from,true,ImpactZone.LeftGlove,false,0f,.01f};
                        int blocks=game.Round.EnemyBlocks; float health=game.Round.EnemyHealth;
                        var resolve=typeof(BoxingGame).GetMethod("ResolvePlayer",flags); resolve.Invoke(game,args);
                        Assert.That(game.Round.EnemyBlocks,Is.EqualTo(blocks+1)); Assert.That(game.Round.EnemyHealth,Is.EqualTo(health));
                        resolve.Invoke(game,args); Assert.That(game.Round.EnemyBlocks,Is.EqualTo(blocks+1));
                        found=true; verified++; break;
                    }
                    Assert.That(found,Is.True,"Need an arm-only contact path for side "+index);
                }
                Assert.That(verified,Is.EqualTo(2)); Assert.That(game.feedback.Sends,Is.Zero);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); }
        }
    }
}
