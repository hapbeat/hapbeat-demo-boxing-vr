using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.Boxing.Editor
{
    public static class BoxingHandPolish
    {
        public const string GhostMaterialPath = "Assets/Boxing/Art/UnityHands/Ghost.mat";
        public const string PrivateHandResource = "HapbeatPrivate/UnityHands/";

        [MenuItem("Hapbeat Boxing/Install Menu Hand Resolvers")]
        public static void ApplyMenuHands()
        {
            BoxingPlaceholderHands.Generate();
            var scene=EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            InstallMenuHands(Object.FindAnyObjectByType<BoxingGame>());
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        }

        // Scene holds only tracking events and the resolver; the skeleton driver and hand model are created at runtime (private mesh or public placeholder).
        public static void InstallMenuHands(BoxingGame game)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if(material==null) { material=new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material,GhostMaterialPath); }
            material.SetColor("_BaseColor",new Color(.45f,.85f,1,.48f));
            material.SetFloat("_Surface",1); material.SetFloat("_ZWrite",0);
            material.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend",(float)BlendMode.OneMinusSrcAlpha);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue=(int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            if(AssetDatabase.LoadAssetAtPath<GameObject>(BoxingPlaceholderHands.PrefabPath(Handedness.Right))==null) BoxingPlaceholderHands.Generate();
            foreach(var old in Object.FindObjectsByType<BoxingMenuHand>(FindObjectsInactive.Include,FindObjectsSortMode.None)) Object.DestroyImmediate(old.gameObject);
            foreach(var side in new[]{Handedness.Left,Handedness.Right})
            {
                var root=new GameObject(side+" menu ghost hand"); root.transform.SetParent(game.input.headCamera.transform.parent,false);
                var tracking=root.AddComponent<XRHandTrackingEvents>(); tracking.handedness=side;
                tracking.updateType=XRHandTrackingEvents.UpdateTypes.Dynamic|XRHandTrackingEvents.UpdateTypes.BeforeRender;
                var visual=root.AddComponent<BoxingMenuHand>(); visual.menu=game.menu; visual.tracking=tracking;
                var resolver=root.AddComponent<BoxingHandModelResolver>();
                resolver.privateResourcePath=PrivateHandResource+side+"Hand";
                resolver.fallbackPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(BoxingPlaceholderHands.PrefabPath(side));
                if(resolver.fallbackPrefab==null) throw new System.InvalidOperationException("Placeholder hand prefab missing: "+BoxingPlaceholderHands.PrefabPath(side));
                resolver.material=material; resolver.menuHand=visual;
            }
        }

        public static void Install(BoxingGame game)
        {
            game.presentation.arena=GameObject.Find("Arena - original procedural assets").transform;
            EditorUtility.SetDirty(game.presentation);
            InstallMenuHands(game);
            var feedback=game.feedback;
            if(feedback.voiceSource==null)
            {
                var voice=new GameObject("Countdown voice"); voice.transform.SetParent(feedback.transform,false);
                feedback.voiceSource=voice.AddComponent<AudioSource>(); feedback.voiceSource.playOnAwake=false; feedback.voiceSource.spatialBlend=0;
            }
            feedback.countdownVoice=new AudioClip[3];
            for(int i=0;i<3;i++) feedback.countdownVoice[i]=AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Boxing/Audio/Voice/{i+1}.ogg");
            feedback.winVoice=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Audio/Voice/you_win.ogg");
            feedback.loseVoice=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Audio/Voice/you_lose.ogg");
            feedback.tieVoice=AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Boxing/Audio/Voice/its_a_tie.ogg");
            BoxingForearmSetup.Install(game.presentation);
            EditorUtility.SetDirty(feedback);
        }
    }
}
