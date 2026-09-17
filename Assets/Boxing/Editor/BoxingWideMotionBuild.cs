using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Android;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR;

namespace Hapbeat.Boxing.Editor
{
    public sealed class BoxingWideMotionBuild : IPreprocessBuildWithReport, IPostGenerateGradleAndroidProject
    {
        public int callbackOrder=>-1000;
        [MenuItem("Hapbeat Boxing/Configure Quest Wide Motion")]
        public static void Configure()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var feature=settings.GetFeature<BoxingWideMotionFeature>();
            if(feature==null) throw new BuildFailedException("Boxing WMM feature was not discovered.");
            feature.enabled=true; EditorUtility.SetDirty(feature); AssetDatabase.SaveAssets();
        }
        public void OnPreprocessBuild(BuildReport report)
        { if(report.summary.platform==BuildTarget.Android) Configure(); }
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest=Path.Combine(path,"src/main/AndroidManifest.xml");
            var xml=new XmlDocument(); xml.Load(manifest); AddManifestEntries(xml); xml.Save(manifest);
        }
        public static void AddManifestEntries(XmlDocument xml)
        {
            const string android="http://schemas.android.com/apk/res/android";
            void Ensure(string tag,string name,bool optional)
            {
                XmlElement found=null;
                foreach(XmlNode child in xml.DocumentElement.ChildNodes)
                    if(child is XmlElement e && e.Name==tag && e.GetAttribute("name",android)==name) {found=e;break;}
                if(found==null) {found=xml.CreateElement(tag); found.SetAttribute("name",android,name); xml.DocumentElement.AppendChild(found);}
                if(optional) found.SetAttribute("required",android,"false");
            }
            Ensure("uses-feature","com.oculus.software.body_tracking",true);
            Ensure("uses-permission",BoxingWideMotionFeature.BodyPermission,false);
        }
    }
}
