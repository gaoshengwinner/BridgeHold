using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using System.IO;

namespace BridgeHold.Editor
{
    public static class BridgeHoldSetup
    {
        [MenuItem("BridgeHold/Create or Open Game Scene")]
        public static void CreateScene()
        {
            const string path = "Assets/BridgeHold/Scenes/Bridge.unity";
            if(File.Exists(path)) EditorSceneManager.OpenScene(path);
            else
            {
            Directory.CreateDirectory("Assets/BridgeHold/Scenes");
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("BridgeHold Game").AddComponent<BridgeHoldGame>();
            EditorSceneManager.SaveScene(scene,path);
            }
            Directory.CreateDirectory("Assets/BridgeHold/Resources");
            EnsureMaterial("BridgeSurface", "Standard");
            EnsureMaterial("BridgeTrail", "Sprites/Default");
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(path,true)};
            PlayerSettings.companyName="BridgeHold";
            PlayerSettings.productName="BridgeHold";
            PlayerSettings.defaultScreenWidth=540; PlayerSettings.defaultScreenHeight=960;
            PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.resizableWindow=true;
            PlayerSettings.defaultInterfaceOrientation=UIOrientation.Portrait;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android,"com.bridgehold.game");
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS,"com.AiStudioWorks.BridgeHold");
            AssetDatabase.SaveAssets();
            Debug.Log("BRIDGEHOLD_SETUP_OK");
        }
        [MenuItem("BridgeHold/Set iOS Bundle ID")]
        public static void SetIOSBundleId()
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS,"com.AiStudioWorks.BridgeHold");
            AssetDatabase.SaveAssets();
            Debug.Log("iOS Bundle ID: " + PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS));
        }
        static void EnsureMaterial(string name,string shader)
        {
            string path="Assets/BridgeHold/Resources/"+name+".mat";
            if(!File.Exists(path)) AssetDatabase.CreateAsset(new Material(Shader.Find(shader)),path);
        }
        [MenuItem("BridgeHold/Build macOS Prototype")]
        public static void BuildMac()
        {
            CreateScene();
            var report=BuildPipeline.BuildPlayer(new[]{"Assets/BridgeHold/Scenes/Bridge.unity"},"Builds/BridgeHold.app",BuildTarget.StandaloneOSX,BuildOptions.Development);
            if(report.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new System.Exception("Build failed: "+report.summary.result);
        }
    }
}
