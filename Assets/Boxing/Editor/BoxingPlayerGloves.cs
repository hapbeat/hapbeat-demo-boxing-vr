using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    public static class BoxingPlayerGloves
    {
        public static void Install(BoxingPresentation view)
        {
            var avatar=view.enemyAvatar;
            Build(view.leftGlove,avatar,avatar.rightHand,"Left");
            Build(view.rightGlove,avatar,avatar.leftHand,"Right");
        }
        private static void Build(Transform root,BoxingOpponentAvatar avatar,Transform hand,string side)
        {
            var source=avatar.skin.sharedMesh;
            int bone=Array.IndexOf(avatar.skin.bones,hand);
            var vertices=source.vertices; var weights=source.boneWeights; var sourceNormals=source.normals;
            var points=new List<Vector3>(); var normals=new List<Vector3>(); var map=new Dictionary<int,int>();
            var normalMatrix=source.bindposes[bone].inverse.transpose;
            Quaternion orientation=Quaternion.LookRotation(Vector3.up,Vector3.forward);
            for(int i=0;i<vertices.Length;i++)
                if(weights[i].boneIndex0==bone && weights[i].weight0>.9f)
                { map[i]=points.Count; points.Add(orientation*source.bindposes[bone].MultiplyPoint3x4(vertices[i]));
                  normals.Add(Vector3.Scale(orientation*normalMatrix.MultiplyVector(sourceNormals[i]),new Vector3(1,1/BoxingOpponentAvatar.GloveDepthRatio,1)).normalized); }
            var bounds=new Bounds(points[0],Vector3.zero);
            foreach(var p in points) bounds.Encapsulate(p);
            float scale=BoxingOpponentAvatar.GloveWidth/bounds.size.x;
            for(int i=0;i<points.Count;i++) points[i]=Vector3.Scale((points[i]-bounds.center)*scale,new Vector3(1,BoxingOpponentAvatar.GloveDepthRatio,1));
            string path=$"Assets/Boxing/Art/BoxerGlove{side}.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if(mesh==null) { mesh=new Mesh(); AssetDatabase.CreateAsset(mesh,path); }
            mesh.Clear(); mesh.name="Boxer glove "+side; mesh.SetVertices(points); mesh.subMeshCount=source.subMeshCount;
            for(int s=0;s<source.subMeshCount;s++)
            {
                var input=source.GetTriangles(s); var output=new List<int>();
                for(int i=0;i<input.Length;i+=3)
                    if(map.ContainsKey(input[i]) && map.ContainsKey(input[i+1]) && map.ContainsKey(input[i+2]))
                    { output.Add(map[input[i]]); output.Add(map[input[i+1]]); output.Add(map[input[i+2]]); }
                mesh.SetTriangles(output,s);
            }
            mesh.SetNormals(normals); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
            for(int i=root.childCount-1;i>=0;i--) UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
            root.localScale=Vector3.one;
            var model=new GameObject("Boxer glove "+side); model.transform.SetParent(root,false);
            model.AddComponent<MeshFilter>().sharedMesh=mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials=avatar.skin.sharedMaterials;
            Debug.Log($"BOXING_SHARED_GLOVE {side} size_m={mesh.bounds.size:F4} collision_diameter_m=0.1400");
        }
    }
}
