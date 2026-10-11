using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public class BoxingEventMapTests
    {
        [TestCase("Assets")] [TestCase("Assets/Boxing/Haptics")] [TestCase("Packages")] [TestCase("")]
        public void ManifestSearchHandlesRootAndStandaloneFolders(string folder)
        {
            var type=Type.GetType("Hapbeat.Editor.HapbeatManifestIntensity, Hapbeat.Editor",true);
            var find=type.GetMethod("FindKitManifest",BindingFlags.NonPublic|BindingFlags.Static);
            Assert.DoesNotThrow(()=>find.Invoke(null,new object[]{folder}));
        }
        [Test] public void MapHasOneEntryPerCueAndSideWithItsOwnStreamClip()
        {
            var map=AssetDatabase.LoadAssetAtPath<Hapbeat.HapbeatEventMap>("Assets/Boxing/Haptics/BoxingEventMap.asset");
            Assert.That(map.entries.Count,Is.EqualTo(18));
            for(int i=0;i<map.entries.Count;i++)
            {
                var entry=map.entries[i];
                string cue=BoxingFeedback.Cues[i<16 ? i%8 : i-8];
                Assert.That(entry.eventName,Is.EqualTo((i<8 ? "left_" : i<16 ? "right_" : "")+cue));
                Assert.That(entry.target,Is.EqualTo(i<8 ? "*/pos_l_wrist" : i<16 ? "*/pos_r_wrist" : "*/pos_neck"));
                Assert.That(AssetDatabase.GetAssetPath(entry.streamClip),Is.EqualTo("Assets/Boxing/Haptics/stream-clips/"+cue+".wav"));
                Assert.That(entry.gain,Is.EqualTo(0.8f)); Assert.That(entry.CachedManifestIntensity,Is.EqualTo(1));
            }
        }
        [Test] public void CuesSeparateTheContactPartAndTheStrength()
        {
            var tuning=ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                string Cue(BoxingImpact impact)=>BoxingFeedback.CueName(impact);
                foreach(var speed in new[]{1f,4f})
                {
                    string level=speed<tuning.hardHitSpeed ? "_soft" : "_hard";
                    // Own punches: the opponent's glove, or anything else (head / body / arms).
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,1)),Is.EqualTo("punch_glove"+level));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,Vector3.zero,tuning,ImpactSurface.Glove,1,ImpactPart.Forearm)),Is.EqualTo("punch_body"+level));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,Vector3.zero,tuning,ImpactSurface.Body,1)),Is.EqualTo("punch_body"+level));
                    // An unprepared punch is always soft.
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,Vector3.zero,tuning,ImpactSurface.Body,0)),Is.EqualTo("punch_body_soft"));
                    // Received: glove, forearm, head / body.
                    Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove)),Is.EqualTo("guard_glove"+level));
                    Assert.That(Cue(new BoxingImpact(ImpactZone.RightGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove,ImpactPart.Forearm)),Is.EqualTo("guard_arm"+level));
                    foreach(var zone in new[]{ImpactZone.Head,ImpactZone.Body})
                    {
                        var hit=new BoxingImpact(zone,speed,false,Vector3.zero,tuning,ImpactSurface.Body);
                        Assert.That(Cue(hit),Is.EqualTo("hit_body"+level));
                        Assert.That(BoxingFeedback.TriggerIndex(hit),Is.EqualTo(speed<tuning.hardHitSpeed ? 16 : 17));
                    }
                    var right=new BoxingImpact(ImpactZone.RightGlove,speed,false,Vector3.zero,tuning,ImpactSurface.Glove,ImpactPart.Forearm);
                    Assert.That(BoxingFeedback.TriggerIndex(right),Is.EqualTo(8+BoxingFeedback.CueIndex(right)));
                }
                // Continuous mode keeps one strength: always the soft cues.
                tuning.impactMode=ImpactMode.Continuous;
                Assert.That(Cue(new BoxingImpact(ImpactZone.Head,5,false,Vector3.zero,tuning,ImpactSurface.Body)),Is.EqualTo("hit_body_soft"));
                Assert.That(Cue(new BoxingImpact(ImpactZone.LeftGlove,5,Vector3.zero,tuning,ImpactSurface.Glove,1)),Is.EqualTo("punch_glove_soft"));
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }
        [Test] public void StandaloneStreamClipManifestLookupDoesNotThrow()
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Haptics/stream-clips/hit_body_hard.wav");
            Assert.That(clip,Is.Not.Null);
            Assert.That(clip.LoadAudioData(),Is.True);
            var samples=new float[clip.samples*clip.channels];
            Assert.That(clip.GetData(samples,0),Is.True);
            Assert.That(samples.Length,Is.GreaterThan(0));
            var type=Type.GetType("Hapbeat.Editor.HapbeatManifestIntensity, Hapbeat.Editor",true);
            var find=type.GetMethod("FindManifestForClip",BindingFlags.Public|BindingFlags.Static);
            Assert.DoesNotThrow(()=>find.Invoke(null,new object[]{clip}));
        }
    }
}
