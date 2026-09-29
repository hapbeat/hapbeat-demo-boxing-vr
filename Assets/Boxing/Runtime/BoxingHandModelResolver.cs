using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing
{
    public enum BoxingHandModelSource { None, Private, Fallback }

    // Menu ghost hand model chosen at runtime. The Unity XR Hands sample mesh is not redistributable as a source asset,
    // so it is loaded from the linked private Resources folder when present; public clones use the placeholder prefab.
    // The skeleton driver is added to the model only after it exists: a serialized driver without a root transform
    // throws from XRHandSkeletonDriver.OnAfterDeserialize when the scene loads off the main thread, and its OnEnable
    // needs the joint references, so the model stays inactive until they are assigned.
    public sealed class BoxingHandModelResolver : MonoBehaviour
    {
        public string privateResourcePath;
        public GameObject fallbackPrefab;
        public Material material;
        public BoxingMenuHand menuHand;
        public XRHandSkeletonDriver Driver { get; private set; }
        public BoxingHandModelSource Source { get; private set; }
        public GameObject Model { get; private set; }
        public readonly List<string> MissingJoints = new List<string>();

        private void Awake() => Resolve();

        public void Resolve()
        {
            if(Model!=null) return;
            var prefab=string.IsNullOrEmpty(privateResourcePath) ? null : Resources.Load<GameObject>(privateResourcePath);
            Source=prefab!=null ? BoxingHandModelSource.Private : BoxingHandModelSource.Fallback;
            if(prefab==null) prefab=fallbackPrefab;
            if(prefab==null) { Source=BoxingHandModelSource.None; Debug.LogError("[Boxing Hands] No menu hand model: "+name,this); return; }
            Model=Instantiate(prefab,transform,false); Model.name=prefab.name; Model.SetActive(false);
            foreach(var animator in Model.GetComponentsInChildren<Animator>(true)) animator.enabled=false;
            Driver=Model.AddComponent<XRHandSkeletonDriver>(); Driver.handTrackingEvents=menuHand.tracking;
            Driver.rootTransform=Model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t=>t.name.EndsWith(XRHandJointID.Wrist.ToString()));
            Driver.jointTransformReferences=new List<JointToTransformReference>();
            if(Driver.rootTransform==null) MissingJoints.Add(XRHandJointID.Wrist.ToString());
            else Driver.FindJointsFromRoot(MissingJoints);
            Driver.InitializeFromSerializedReferences();
            if(MissingJoints.Count!=0) Debug.LogError("[Boxing Hands] Missing ghost hand joints ("+Source+"): "+string.Join(",",MissingJoints),this);
            var renderers=Model.GetComponentsInChildren<Renderer>(true);
            foreach(var renderer in renderers)
            {
                renderer.sharedMaterial=material; renderer.enabled=false; renderer.shadowCastingMode=ShadowCastingMode.Off;
                if(renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen=true;
            }
            menuHand.renderers=renderers;
            Model.SetActive(true);
            Debug.Log("[Boxing Hands] "+name+" uses "+Source+" model "+prefab.name);
        }
    }
}
