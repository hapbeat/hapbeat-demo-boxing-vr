using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Editor
{
    public static class BoxingAvatarSetup
    {
        public const string ModelPath = "Assets/Boxing/Art/Boxer.fbx";
        [MenuItem("Hapbeat Boxing/Install Blender Opponent")]
        public static void Install()
        {
            var scene = EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            var game = Object.FindFirstObjectByType<BoxingGame>();
            var dwell = game.menu.panel.Find("Look to select progress");
            if (dwell != null) Object.DestroyImmediate(dwell.gameObject);
            game.tuning.gloveRadius = .07f; game.tuning.enemyHeadRadius = .145f; game.tuning.enemyBodyRadius = .21f;
            EditorUtility.SetDirty(game.tuning);
            // The named old opponent is the explicit replacement target, not the arena or player rig.
            var old = GameObject.Find("Opponent - sparring partner");
            if (old != null) Object.DestroyImmediate(old);
            // Explicit reinstall refreshes mesh-derived centres/rest data after re-export.
            if (game.presentation.enemyAvatar != null) Object.DestroyImmediate(game.presentation.enemyAvatar.gameObject);
            Attach(game.presentation);
            BoxingPlayerGloves.Install(game.presentation);
            BoxingHandPolish.Install(game);
            game.Initialize(); game.presentation.enemyAvatar.Render(game.Opponent, 0);
            EditorUtility.SetDirty(game.presentation);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            BoxingProject.Validate(); Preview();
        }
        public static void Attach(BoxingPresentation view)
        {
            if (view.enemyAvatar != null) return;
            var root = new GameObject("Opponent - Blender boxer");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            instance.transform.SetParent(root.transform, false);
            // Runtime inverse kinematics owns the bones; imported sample clips are not played over it.
            foreach (var animator in instance.GetComponentsInChildren<Animator>()) animator.enabled = false;
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            var colors = new[] { new Color(.72f,.56f,.43f), new Color(.10f,.21f,.30f), new Color(.88f,.86f,.77f), new Color(.85f,.15f,.10f), new Color(.20f,.26f,.30f), new Color(.08f,.10f,.12f) };
            var materials = new Material[skin.sharedMaterials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                string path = $"Assets/Boxing/Art/BoxerMaterial{i}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
                mat.SetColor("_BaseColor", colors[i]); mat.SetFloat("_Smoothness", i == 3 ? .4f : .15f); materials[i] = mat; EditorUtility.SetDirty(mat);
            }
            skin.sharedMaterials = materials;
            var avatar = root.AddComponent<BoxingOpponentAvatar>(); avatar.CaptureRest(); view.enemyAvatar = avatar;
            EditorUtility.SetDirty(view); EditorUtility.SetDirty(avatar);
        }
        public static void Preview()
        {
            BoxingProject.Validate();
            var game = Object.FindFirstObjectByType<BoxingGame>(); game.Initialize();
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false); game.menu.Close();
            var pose = new BoxerPose { valid=true, head=new Vector3(0,1.65f,.2f), headRotation=Quaternion.identity,
                left=new Vector3(-.24f,1.25f,.5f), right=new Vector3(.24f,1.25f,.5f), leftRotation=Quaternion.identity, rightRotation=Quaternion.identity };
            var camera = game.input.headCamera;
            camera.transform.SetPositionAndRotation(new Vector3(1.8f, 1.8f, -1.5f), Quaternion.LookRotation(new Vector3(-1.8f,-.8f,2.58f)));
            game.presentation.Render(game,pose,true); Directory.CreateDirectory("Logs");
            Capture(game,camera,"Logs/boxing-avatar-rest.png");
            game.presentation.enemyAvatar.React(false, 1); game.presentation.Render(game,pose,true);
            Capture(game,camera,"Logs/boxing-avatar-head-hit.png");
            game.presentation.enemyAvatar.ResetReaction(); game.presentation.enemyAvatar.React(true, 1); game.presentation.Render(game,pose,true);
            Capture(game,camera,"Logs/boxing-avatar-body-hit.png");
            game.presentation.enemyAvatar.ResetReaction();
            int captured = 0;
            for(int i=0;i<12000 && captured!=15;i++)
            {
                game.Opponent.Tick(.005f,pose.head,true);
                int bit=1<<(int)game.Opponent.Attack;
                if(!game.Opponent.Striking || game.Opponent.MotionWeight<.8f || (captured&bit)!=0) continue;
                game.presentation.Render(game,pose,true);
                Capture(game,camera,"Logs/boxing-attack-"+game.Opponent.Attack+".png"); captured|=bit;
            }
            game.Opponent.Reset(1.65f);
            for(int i=0;i<1000 && !game.Opponent.Striking;i++) game.Opponent.Tick(.01f,pose.head,true);
            game.Opponent.Tick(game.tuning.strikeSeconds*.75f,pose.head,true);
            game.presentation.Render(game,pose,true); Capture(game,camera,"Logs/boxing-avatar-strike.png");
            game.Opponent.Reset(1.65f);
            bool hookCaptured=false, crossCaptured=false;
            for(int i=0;i<5000 && !(hookCaptured && crossCaptured);i++)
            {
                game.Opponent.Tick(.01f,pose.head,true);
                if(!game.Opponent.Striking || game.Opponent.MotionWeight<.65f) continue;
                if(game.Opponent.Hook && !hookCaptured)
                { game.presentation.Render(game,pose,true); Capture(game,camera,"Logs/boxing-avatar-hook.png"); hookCaptured=true; }
                if(!game.Opponent.Hook && !game.Opponent.AttackLeft && !crossCaptured)
                { game.presentation.Render(game,pose,true); Capture(game,camera,"Logs/boxing-avatar-cross.png"); crossCaptured=true; }
            }
            camera.transform.SetPositionAndRotation(pose.head,Quaternion.identity);
            game.Opponent.Reset(1.65f); game.presentation.Render(game,pose,true);
            Capture(game,camera,"Logs/boxing-avatar-player-view.png");
            int guards=0;
            for(int i=0;i<30000 && guards!=15;i++)
            {
                game.Opponent.Tick(.01f,pose.head,true);
                int bit=1<<(int)game.Opponent.Guard;
                if(game.Opponent.GuardWeight<.999f || (guards&bit)!=0) continue;
                game.presentation.Render(game,pose,true);
                Capture(game,camera,"Logs/boxing-guard-"+game.Opponent.Guard+".png"); guards|=bit;
            }
            if(guards!=15) throw new System.InvalidOperationException("Missing four-way guard preview");
            game.presentation.enemyAvatar.gameObject.SetActive(false);
            game.presentation.leftGlove.gameObject.SetActive(true); game.presentation.rightGlove.gameObject.SetActive(true);
            game.presentation.leftGlove.SetPositionAndRotation(new Vector3(-.12f,1.4f,.3f),Quaternion.LookRotation(Vector3.up,Vector3.back));
            game.presentation.rightGlove.SetPositionAndRotation(new Vector3(.12f,1.4f,.3f),Quaternion.LookRotation(Vector3.up,Vector3.forward));
            camera.transform.SetPositionAndRotation(new Vector3(0,1.4f,-.3f),Quaternion.identity); camera.fieldOfView=40;
            Capture(game,camera,"Logs/boxing-gloves-back-palm.png");
            if(!hookCaptured || !crossCaptured) throw new System.InvalidOperationException("Missing hook/cross preview");
            Debug.Log("BOXING_AVATAR_PREVIEW: rest, head hit, body hit, straight, cross, hook and player view rendered silently");
        }
        private static void Capture(BoxingGame game, Camera camera, string path)
        {
            // Editor render requests in one frame can reuse cached skinning matrices.
            // Bake the current bones for these offline stills; Play Mode uses the real renderer.
            var skin = game.presentation.enemyAvatar.skin; var mesh = new Mesh(); skin.BakeMesh(mesh);
            var baked = new GameObject("Temporary preview skin"); baked.transform.SetParent(skin.transform,false);
            baked.transform.localScale = new Vector3(1 / skin.transform.localScale.x, 1 / skin.transform.localScale.y, 1 / skin.transform.localScale.z);
            baked.AddComponent<MeshFilter>().sharedMesh=mesh; baked.AddComponent<MeshRenderer>().sharedMaterials=skin.sharedMaterials;
            skin.enabled=false;
            try { BoxingVerification.Render(camera,path); }
            finally { skin.enabled=true; Object.DestroyImmediate(baked); Object.DestroyImmediate(mesh); }
        }
        public static void Inspect()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.optimizeGameObjects = false; importer.isReadable = true;
            importer.SaveAndReimport();
            var instance = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath));
            foreach (var t in instance.GetComponentsInChildren<Transform>())
                Debug.Log($"BOXER_IMPORT {t.name} world={t.position:F3} local={t.localPosition:F3} rot={t.eulerAngles:F1} scale={t.lossyScale:F3}");
            var skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
            Debug.Log($"BOXER_MESH bounds={skin.bounds} materials={string.Join(",", skin.sharedMaterials.Select(m => m.name))}");
            Object.DestroyImmediate(instance);
        }
    }
}
