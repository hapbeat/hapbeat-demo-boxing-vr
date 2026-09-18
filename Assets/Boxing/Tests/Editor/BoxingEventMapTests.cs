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
        [Test] public void SimplifiedMapPreservesCustomDamageAndCorrectReceiverRouting()
        {
            var map=AssetDatabase.LoadAssetAtPath<Hapbeat.HapbeatEventMap>("Assets/Boxing/Haptics/BoxingEventMap.asset");
            Assert.That(map.entries.Count,Is.EqualTo(10));
            Assert.That(map.entries.Any(e=>e.eventName.StartsWith("head_glove_")),Is.False);
            var tuning=ScriptableObject.CreateInstance<BoxingTuning>();
            try
            {
                foreach(var zone in new[]{ImpactZone.Head,ImpactZone.Body})
                foreach(var speed in new[]{1f,5f})
                {
                    var impact=new BoxingImpact(zone,speed,false,Vector3.zero,tuning,ImpactSurface.Body);
                    var entry=map.entries[BoxingFeedback.TriggerIndex(impact)];
                    Assert.That(entry.eventName,Is.EqualTo(speed<2.5f ? "head_body_soft" : "head_body_hard"));
                    Assert.That(entry.target,Is.EqualTo("*/pos_neck"));
                    if(speed>=2.5f) Assert.That(AssetDatabase.GetAssetPath(entry.streamClip),Is.EqualTo("Assets/Boxing/Haptics/damage.wav"));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }
        [Test] public void StandaloneDamageClipManifestLookupDoesNotThrow()
        {
            var clip=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Haptics/damage.wav");
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
