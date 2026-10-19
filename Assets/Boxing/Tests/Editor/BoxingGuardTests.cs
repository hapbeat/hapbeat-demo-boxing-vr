using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingGuardTests
    {
        private BoxingTuning tuning;
        [SetUp] public void Setup()=>tuning=ScriptableObject.CreateInstance<BoxingTuning>();
        [TearDown] public void Cleanup()=>Object.DestroyImmediate(tuning);
        // A hook guard covers one side (GuardSide), with the glove at the temple for a head hook; a body hook guard's cover is
        // the dropped forearm, checked on the rendered arms (BoxingGuardMotionTests), so here only its glove side is checked.
        [Test] public void AllFourGuardsInterceptTheirIntendedDirectionBeforeTheBody()
        {
            var enemy=new BoxingOpponent(tuning, 11); int seen=0;
            for(int i=0;i<30000 && seen!=15;i++)
            {
                enemy.Tick(.01f,Vector3.up*1.65f,Quaternion.identity,true);
                if(enemy.GuardWeight<.999f) continue;
                Vector3 target=enemy.BodyGuard ? enemy.Body : enemy.Head;
                float radius=enemy.BodyGuard ? tuning.enemyBodyRadius : tuning.enemyHeadRadius;
                foreach(int side in new[]{-1,1})
                {
                    if(enemy.HookGuard && side!=(int)enemy.GuardSide) continue;
                    if(enemy.Guard==BoxingGuard.BodyHook)
                    {
                        Vector3 cover=side<0 ? enemy.Left : enemy.Right, other=side<0 ? enemy.Right : enemy.Left;
                        Assert.That((cover.x-enemy.Head.x)*side,Is.GreaterThan((other.x-enemy.Head.x)*-side),"The body hook guard's glove moves to its side.");
                        Assert.That(cover.y,Is.LessThan(other.y),"Its elbow drops, the glove with it.");
                        continue;
                    }
                    Vector3 from=target+(enemy.HookGuard ? Vector3.right*side : Vector3.back)*.8f;
                    Assert.That(BoxingCollision.Sweep(from,target,tuning.gloveRadius,target,target,radius,out float bodyTime),Is.True);
                    float first=float.PositiveInfinity;
                    foreach(var hand in new[]{enemy.Left,enemy.Right})
                        if(BoxingCollision.Sweep(from,target,tuning.gloveRadius,hand,hand,tuning.gloveRadius,out float t)) first=Mathf.Min(first,t);
                    Assert.That(first,Is.LessThan(bodyTime),enemy.Guard+" must cover the incoming path.");
                }
                seen|=1<<(int)enemy.Guard;
            }
            Assert.That(seen,Is.EqualTo(15));
        }
        [Test] public void NpcRaisesGuardAfterRetractingAPunch()
        {
            var enemy=new BoxingOpponent(tuning, 11);
            // Wait for the whole first attack (a combo has several strikes) to finish retracting.
            for(int i=0;i<1000 && (enemy.CompletedAttacks==0 || enemy.State!=BoxingOpponentState.Neutral);i++) enemy.Tick(.01f,Vector3.up*1.65f,Quaternion.identity,true);
            Assert.That(enemy.CompletedAttacks,Is.GreaterThanOrEqualTo(1)); Assert.That(enemy.State,Is.EqualTo(BoxingOpponentState.Neutral));
            for(int i=0;i<20;i++) enemy.Tick(.01f,Vector3.up*1.65f,Quaternion.identity,true);
            Assert.That(enemy.Guarding,Is.True); Assert.That(enemy.GuardWeight,Is.GreaterThan(.9f));
        }
        [Test] public void LowWidePreparationBiasesBodyHookGuardButLeavesOtherChoices()
        {
            var enemy=new BoxingOpponent(tuning, 11); int matches=0,total=0,seen=0; bool was=false;
            for(int i=0;i<40000;i++)
            {
                var head=Vector3.up*1.65f;
                enemy.ObservePlayer(new BoxerPose{valid=true,head=head,left=head+new Vector3(-.4f,-.4f,.3f),right=head+new Vector3(.4f,-.4f,.3f)},.01f);
                enemy.Tick(.01f,head,Quaternion.identity,true);
                if(enemy.Guarding && !was) {total++; if(enemy.Guard==BoxingGuard.BodyHook) matches++; seen|=1<<(int)enemy.Guard;}
                was=enemy.Guarding;
            }
            Assert.That(total,Is.GreaterThan(40)); Assert.That(matches/(float)total,Is.InRange(.45f,.85f));
            Assert.That(seen,Is.EqualTo(15));
        }
    }
}
