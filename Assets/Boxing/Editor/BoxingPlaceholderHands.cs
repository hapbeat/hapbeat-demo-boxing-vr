using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing.Editor
{
    // Public-safe menu hands for clones without the private Unity XR Hands meshes. Original primitive geometry only.
    // Joint names follow the XR Hands "<L|R>_<XRHandJointID>" layout so XRHandSkeletonDriver.FindJointsFromRoot maps every joint;
    // bones point along local +Z (toward the fingertip) with +Y on the back of the hand, matching the driver's joint poses.
    public static class BoxingPlaceholderHands
    {
        public const string Folder = "Assets/Boxing/Art/PlaceholderHands";
        public static string PrefabPath(Handedness side) => Folder + "/" + side + "PlaceholderHand.prefab";

        // Left-hand layout in metres (thumb on +X). Right hand mirrors X and yaw. Values are rounded adult proportions.
        private static readonly (XRHandFingerID finger, Vector3 basePosition, float yaw, float[] lengths, float radius)[] Fingers =
        {
            (XRHandFingerID.Thumb, new Vector3(.028f,-.016f,.030f), 42, new[]{.036f,.032f,.025f}, .0105f),
            (XRHandFingerID.Index, new Vector3(.020f,-.009f,.034f), 4, new[]{.062f,.039f,.024f,.021f}, .0090f),
            (XRHandFingerID.Middle, new Vector3(.004f,-.008f,.034f), 0, new[]{.062f,.043f,.027f,.023f}, .0092f),
            (XRHandFingerID.Ring, new Vector3(-.013f,-.007f,.033f), -5, new[]{.056f,.039f,.026f,.022f}, .0086f),
            (XRHandFingerID.Little, new Vector3(-.025f,-.009f,.031f), -14, new[]{.048f,.031f,.020f,.020f}, .0076f),
        };

        [MenuItem("Hapbeat Boxing/Generate Placeholder Hands")]
        public static void Generate()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            var material=AssetDatabase.LoadAssetAtPath<Material>(BoxingHandPolish.GhostMaterialPath);
            foreach(var side in new[]{Handedness.Left,Handedness.Right})
            {
                var root=Build(side,material);
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath(side));
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        private static GameObject Build(Handedness side, Material material)
        {
            float mirror=side==Handedness.Left ? 1 : -1;
            string prefix=side==Handedness.Left ? "L_" : "R_";
            var root=new GameObject(side+"PlaceholderHand");
            var wrist=Joint(prefix,XRHandJointID.Wrist,root.transform,Vector3.zero,Quaternion.identity);
            Shape("Heel of hand",PrimitiveType.Sphere,wrist,new Vector3(0,-.004f,.008f),new Vector3(.052f,.026f,.036f),material);
            var palm=Joint(prefix,XRHandJointID.Palm,wrist,new Vector3(0,-.009f,.050f),Quaternion.identity);
            Shape("Palm pad",PrimitiveType.Sphere,palm,Vector3.zero,new Vector3(.074f,.024f,.078f),material);
            foreach(var finger in Fingers)
            {
                var basePosition=finger.basePosition; basePosition.x*=mirror;
                var rotation=Quaternion.Euler(finger.finger==XRHandFingerID.Thumb ? 12 : 0,finger.yaw*mirror,finger.finger==XRHandFingerID.Thumb ? -38*mirror : 0);
                var parent=wrist; var position=basePosition;
                int first=finger.finger.GetFrontJointID().ToIndex(), last=finger.finger.GetBackJointID().ToIndex();
                for(int index=first;index<=last;index++)
                {
                    var joint=Joint(prefix,XRHandJointIDUtility.FromIndex(index),parent,position,index==first ? rotation : Quaternion.Euler(8,0,0));
                    int bone=index-first;
                    if(index<last)
                    {
                        float length=finger.lengths[bone], radius=finger.radius*(1-.08f*bone);
                        var segment=Shape("Bone",PrimitiveType.Capsule,joint,Vector3.zero,Vector3.one,material);
                        BoxingPresentation.Segment(segment,joint.position,joint.TransformPoint(Vector3.forward*length),radius);
                        position=Vector3.forward*length;
                    }
                    else Shape("Tip",PrimitiveType.Sphere,joint,Vector3.back*finger.radius*.35f,Vector3.one*finger.radius*1.5f,material);
                    parent=joint;
                }
            }
            return root;
        }
        private static Transform Joint(string prefix, XRHandJointID id, Transform parent, Vector3 position, Quaternion rotation)
        {
            var joint=new GameObject(prefix+id).transform; joint.SetParent(parent,false);
            joint.localPosition=position; joint.localRotation=rotation; return joint;
        }
        private static Transform Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var shape=GameObject.CreatePrimitive(type); shape.name=name; shape.transform.SetParent(parent,false);
            Object.DestroyImmediate(shape.GetComponent<Collider>());
            shape.transform.localPosition=position; shape.transform.localScale=scale;
            var renderer=shape.GetComponent<MeshRenderer>(); renderer.sharedMaterial=material; renderer.shadowCastingMode=ShadowCastingMode.Off;
            return shape.transform;
        }
    }
}
