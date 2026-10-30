using System.IO;
using UnityEditor;
using UnityEditor.Build;
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
        const string IconPath = "Assets/Art/icon.png";

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
            EnsureArtImported();
            ConfigureIcons();
        }

        /// <summary>
        /// 强制按 <see cref="ArtImportSettings"/> 的口径重新导入三张图。
        /// 不做这一步的话，如果美术先于 AssetPostprocessor 落进工程（或 .meta 已存在），
        /// 就会沿用旧导入参数（mipmap + 平台压缩），字标边缘会脏。
        /// </summary>
        static void EnsureArtImported()
        {
            foreach (var p in ArtImportSettings.ArtPaths)
            {
                if (!File.Exists(p)) { Debug.LogWarning("[ILVES-BUILD] 美术文件缺失：" + p); continue; }
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 应用图标：工程里只维护一张母版 <c>Assets/Art/icon.png</c>（1024×1024），
        /// 构建时按 Unity 给每个平台开出的槽位数组统一指过去，缩放交给 Unity。
        /// 这样换图标只换那一张图，不用逐槽位手工拖。
        /// </summary>
        static void ConfigureIcons()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogWarning("[ILVES-BUILD] 缺少图标 " + IconPath + "，跳过图标设置（会用 Unity 默认图标）");
                return;
            }
            int slots = 0;
            slots += ApplyIcons(NamedBuildTarget.Standalone, icon);
            slots += ApplyIcons(NamedBuildTarget.Android, icon);
            Debug.Log("[ILVES-BUILD] 图标已设置 " + IconPath + " (" + icon.width + "×" + icon.height + ") → slots=" + slots);
        }

        static int ApplyIcons(NamedBuildTarget target, Texture2D icon)
        {
            int[] sizes;
            try { sizes = PlayerSettings.GetIconSizes(target, IconKind.Application); }
            catch (System.Exception e)
            {
                Debug.LogWarning("[ILVES-BUILD] 读取 " + target + " 图标槽位失败（跳过）：" + e.Message);
                return 0;
            }
            if (sizes == null || sizes.Length == 0) return 0;
            var arr = new Texture2D[sizes.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = icon;
            PlayerSettings.SetIcons(target, arr, IconKind.Application);
            return arr.Length;
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