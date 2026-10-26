using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ilves.EditorTools
{
    /// <summary>批处理入口：创建 Main 场景并构建 Windows demo。</summary>
    public static class DemoBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string BuildPath = "Builds/Windows/Ilves.exe";
        const string AndroidPath = "Builds/Android/Ilves.apk";

        public static void BuildDemo()
        {
            try
            {
                ConfigurePlayer();
                CreateScene();
                var dir = Path.GetDirectoryName(BuildPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log("[ILVES-BUILD] result=" + s.result + " size=" + s.totalSize + " errors=" + s.totalErrors + " output=" + s.outputPath);
                EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ILVES-BUILD] EXCEPTION " + e);
                EditorApplication.Exit(1);
            }
        }


        /// <summary>批处理入口：构建 Android APK（只出 ARM64，横屏）。</summary>
        public static void BuildAndroidDemo()
        {
            try
            {
                ConfigurePlayer();
                ConfigureAndroid();
                CreateScene();
                var dir = Path.GetDirectoryName(AndroidPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = AndroidPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log("[ILVES-BUILD-ANDROID] result=" + s.result + " size=" + s.totalSize + " errors=" + s.totalErrors + " output=" + s.outputPath);
                EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ILVES-BUILD-ANDROID] EXCEPTION " + e);
                EditorApplication.Exit(1);
            }
        }

        static void ConfigureAndroid()
        {
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.understory.ilves");
            PlayerSettings.Android.bundleVersionCode = 3;   // 0.2.1
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Ilves";
            PlayerSettings.companyName = "Understory";
            PlayerSettings.bundleVersion = "0.2.1";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.understory.ilves");
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("DemoBootstrap");
            go.AddComponent<Ilves.UI.DemoBootstrap>();

            var camGo = new GameObject("BackgroundCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.043f, 0.047f, 0.031f);
            cam.orthographic = true;
            cam.cullingMask = 0;

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log("[ILVES-BUILD] scene saved: " + ScenePath);
        }
    }
}