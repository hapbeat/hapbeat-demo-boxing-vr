using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    public static class BoxingForearmSetup
    {
        [MenuItem("Hapbeat Boxing/Install Forearms And Result Voice")]
        public static void Apply()
        {
            var scene=EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game=Object.FindFirstObjectByType<BoxingGame>();
            BoxingHandPolish.Install(game);
            // Health bars are generated at runtime, never authored into the scene.
            var cue=game.presentation.cueText.transform;
            for(int i=cue.childCount-1;i>=0;i--)
                if(cue.GetChild(i).name=="YOU HP" || cue.GetChild(i).name=="OPPONENT HP") Object.DestroyImmediate(cue.GetChild(i).gameObject);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate();
            // Validate reloads the saved scene; use that scene's objects for preview.
            game=Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
            // Preview only; do not persist this demonstration pose into the scene.
            var pose=new BoxerPose { valid=true, head=Vector3.up*1.65f,
                left=new Vector3(-.20f,1.6f,.4f),right=new Vector3(.20f,1.6f,.4f),
                leftRotation=Quaternion.Euler(-55,0,0),rightRotation=Quaternion.Euler(-55,0,0) };
            game.feedback.forceSilent=true; game.menu.Close(); game.presentation.Render(game,pose,false);
            game.input.headCamera.transform.SetPositionAndRotation(pose.head,Quaternion.identity);
            System.IO.Directory.CreateDirectory("Logs");
            BoxingVerification.Render(game.input.headCamera,"Logs/boxing-forearms.png");
        }
        public static void Install(BoxingPresentation view)
        {
            Transform Build(Transform old,string name)
            {
                if(old!=null) Object.DestroyImmediate(old.gameObject);
                var arm=GameObject.CreatePrimitive(PrimitiveType.Capsule); arm.name=name;
                arm.transform.SetParent(view.transform,false);
                // Gameplay uses swept spheres, like all other boxing contacts; no physics callbacks.
                Object.DestroyImmediate(arm.GetComponent<Collider>());
                const string path="Assets/Boxing/Art/PlayerForearm.asset";
                var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if(mesh==null)
                {
                    mesh=Object.Instantiate(arm.GetComponent<MeshFilter>().sharedMesh); mesh.name="PlayerForearm";
                    var vertices=mesh.vertices;
                    for(int i=0;i<vertices.Length;i++)
                    {
                        var p=vertices[i]; float y=Mathf.Abs(p.y);
                        p.x*=BoxingForearm.Radius*2; p.z*=BoxingForearm.Radius*2;
                        p.y=Mathf.Sign(p.y)*(y>.5f ? BoxingForearm.Length*.5f-BoxingForearm.Radius+(y-.5f)*BoxingForearm.Radius*2 : y*(BoxingForearm.Length-2*BoxingForearm.Radius));
                        vertices[i]=p;
                    }
                    mesh.vertices=vertices; mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path);
                }
                arm.GetComponent<MeshFilter>().sharedMesh=mesh;
                arm.GetComponent<Renderer>().sharedMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Boxing/Art/BoxerMaterial0.mat");
                arm.SetActive(false);
                return arm.transform;
            }
            view.leftForearm=Build(view.leftForearm,"Left forearm guard");
            view.rightForearm=Build(view.rightForearm,"Right forearm guard");
            EditorUtility.SetDirty(view);
        }
    }
}
