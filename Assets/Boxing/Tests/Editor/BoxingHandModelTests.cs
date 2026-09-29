using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingHandModelTests
    {
        private BoxingHandModelResolver[] resolvers;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(Editor.BoxingProject.ScenePath);
            resolvers=Object.FindObjectsByType<BoxingHandModelResolver>(FindObjectsInactive.Include,FindObjectsSortMode.None);
        }
        [TearDown] public void Cleanup()=>EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        private static void AssertAllJointsMapped(BoxingHandModelResolver resolver)
        {
            Assert.That(resolver.MissingJoints,Is.Empty,resolver.name);
            var joints=resolver.Driver.jointTransformReferences;
            Assert.That(joints.Select(j=>j.xrHandJointID).Distinct().Count(),Is.EqualTo(XRHandJointID.EndMarker.ToIndex()),resolver.name);
            Assert.That(joints.All(j=>j.jointTransform!=null && j.jointTransform.IsChildOf(resolver.Model.transform)),Is.True,resolver.name);
            Assert.That(resolver.Driver.rootTransform,Is.EqualTo(joints.Single(j=>j.xrHandJointID==XRHandJointID.Wrist).jointTransform));
            Assert.That(resolver.menuHand.renderers,Is.Not.Empty);
            Assert.That(resolver.menuHand.renderers.All(r=>r.sharedMaterial==resolver.material && !r.enabled),Is.True);
        }
        [Test] public void SceneHoldsResolversInsteadOfHandModels()
        {
            Assert.That(resolvers.Length,Is.EqualTo(2));
            foreach(var resolver in resolvers)
            {
                Assert.That(resolver.fallbackPrefab,Is.Not.Null); Assert.That(resolver.Model,Is.Null);
                Assert.That(resolver.GetComponentInChildren<XRHandSkeletonDriver>(true),Is.Null,"Driver must not exist before the model is instantiated.");
                Assert.That(resolver.GetComponentsInChildren<Renderer>(true),Is.Empty,"Hand meshes are not serialized in the scene.");
            }
            foreach(var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            foreach(var transform in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(PrefabUtility.IsPrefabAssetMissing(transform.gameObject),Is.False,"Missing prefab: "+transform.name);
                var source=PrefabUtility.GetCorrespondingObjectFromSource(transform.gameObject);
                if(source!=null) Assert.That(AssetDatabase.GetAssetPath(source),Does.Not.EndWith("Hand.fbx"),"XR Hands meshes must not be scene prefab instances: "+transform.name);
            }
        }
        [Test] public void WithoutPrivateAssetsFallbackMapsEveryJoint()
        {
            foreach(var resolver in resolvers)
            {
                resolver.privateResourcePath="HapbeatPrivate/NotLinked/"+resolver.name;
                resolver.Resolve();
                Assert.That(resolver.Source,Is.EqualTo(BoxingHandModelSource.Fallback));
                AssertAllJointsMapped(resolver);
            }
        }
        [Test] public void LinkedPrivateAssetsAreUsed()
        {
            foreach(var resolver in resolvers)
            {
                Assume.That(Resources.Load<GameObject>(resolver.privateResourcePath),Is.Not.Null,"Private hand meshes are not linked in this checkout.");
                resolver.Resolve();
                Assert.That(resolver.Source,Is.EqualTo(BoxingHandModelSource.Private));
                AssertAllJointsMapped(resolver);
            }
        }
    }
}
