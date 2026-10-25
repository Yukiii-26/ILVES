using System;
using UnityEditor;
using UnityEngine;

namespace Ilves.EditorTools
{
    /// <summary>
    /// ILVES 工程一次性初始化脚本（批处理调用，仅设置工程元信息，不写游戏内容）。
    /// 用法：
    ///   Unity.exe -batchmode -quit -projectPath "D:\UnityProjects\ILVES" \
    ///     -executeMethod Ilves.EditorTools.ProjectBootstrap.Apply -logFile &lt;log&gt;
    /// 设计说明：保留在工程内以便复现/重新套用；不会在正常编辑时自动执行。
    /// </summary>
    public static class ProjectBootstrap
    {
        private const string Tag = "[ILVES-BOOTSTRAP]";

        public static void Apply()
        {
            try
            {
                PlayerSettings.productName = "Ilves";
                PlayerSettings.companyName = "Understory";
                PlayerSettings.bundleVersion = "0.1.0";
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Standalone, "com.understory.ilves");
                EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode2D;
                AssetDatabase.SaveAssets();

                string appId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Standalone);
                Debug.Log(Tag + " RESULT productName=" + PlayerSettings.productName
                          + " | companyName=" + PlayerSettings.companyName
                          + " | bundleVersion=" + PlayerSettings.bundleVersion
                          + " | appIdStandalone=" + appId
                          + " | defaultBehaviorMode=" + EditorSettings.defaultBehaviorMode);
            }
            catch (Exception e)
            {
                Debug.LogError(Tag + " FAILED: " + e);
                EditorApplication.Exit(1);
            }
        }
    }
}
