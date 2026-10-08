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
        public const string ModelPath="Assets/Boxing/Art/PlayerForearm.fbx";
        public static void Install(BoxingPresentation view)
        {
            // Mesh +Y is the hand side and the origin the length midpoint, as RenderForearm places it.
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath).GetComponentInChildren<MeshFilter>();
            var imported=source.GetComponent<MeshRenderer>().sharedMaterials;
            var materials=new Material[imported.Length];
            for(int i=0;i<materials.Length;i++)
                materials[i]=AssetDatabase.LoadAssetAtPath<Material>(BoxingAvatarSetup.MaterialPath(BoxingAvatarSetup.Slot(imported[i])));
            Transform Build(Transform old,string name)
            {
                if(old!=null) Object.DestroyImmediate(old.gameObject);
                // Gameplay uses swept spheres, like all other boxing contacts; no collider.
                var arm=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
                arm.transform.SetParent(view.transform,false);
                arm.GetComponent<MeshFilter>().sharedMesh=source.sharedMesh;
                arm.GetComponent<MeshRenderer>().sharedMaterials=materials;
                arm.SetActive(false);
                return arm.transform;
            }
            view.leftForearm=Build(view.leftForearm,"Left forearm guard");
            view.rightForearm=Build(view.rightForearm,"Right forearm guard");
            EditorUtility.SetDirty(view);
        }
    }
}
