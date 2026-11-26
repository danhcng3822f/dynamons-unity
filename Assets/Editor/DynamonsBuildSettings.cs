using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor.Android;
#endif

namespace Dynamons
{
    /// <summary>Configures Standalone and Android (ARM64) player builds for Dynamons Next with splash disabled, IL2CPP, and custom app icons.</summary>
    [InitializeOnLoad]
    internal sealed class DynamonsBuildSettings : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        static DynamonsBuildSettings() => Apply();

        public void OnPreprocessBuild(BuildReport report) => Apply();

        public static void Apply()
        {
            PlayerSettings.companyName = "Dynamons";
            PlayerSettings.productName = "Dynamons Next";

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.runInBackground = true;

            ConfigureTarget(NamedBuildTarget.Standalone);
            ConfigureTarget(NamedBuildTarget.Android);
            ConfigureIcons();

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.dynamons.game");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.dynamons.game");
            if (int.TryParse(System.Environment.GetEnvironmentVariable("GITHUB_RUN_NUMBER"), out int runNum) && runNum > 0)
            {
                PlayerSettings.bundleVersion = $"1.0.{runNum}";
                PlayerSettings.Android.bundleVersionCode = 100 + runNum;
            }
            else
            {
                if (string.IsNullOrEmpty(PlayerSettings.bundleVersion))
                    PlayerSettings.bundleVersion = "1.0.0";
                if (PlayerSettings.Android.bundleVersionCode < 1)
                    PlayerSettings.Android.bundleVersionCode = 1;
            }
            var desiredArch = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;
            if (PlayerSettings.Android.targetArchitectures != desiredArch)
                PlayerSettings.Android.targetArchitectures = desiredArch;
            PlayerSettings.Android.forceInternetPermission = true;
        }

        private static void ConfigureTarget(NamedBuildTarget target)
        {
            if (PlayerSettings.GetScriptingBackend(target) != ScriptingImplementation.IL2CPP)
                PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            if (PlayerSettings.GetManagedStrippingLevel(target) != ManagedStrippingLevel.Minimal)
                PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.Minimal);
        }

        private static void ConfigureIcons()
        {
            var iconTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Dynamons/Art/splash/app_icon.png");
            if (iconTex == null) return;
            foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android })
            {
                int[] sizes = PlayerSettings.GetIconSizes(target, IconKind.Application);
                if (sizes == null || sizes.Length == 0) continue;
                var icons = new Texture2D[sizes.Length];
                for (int i = 0; i < icons.Length; i++) icons[i] = iconTex;
                PlayerSettings.SetIcons(target, icons, IconKind.Application);
            }
        }
    }

#if UNITY_ANDROID
    /// <summary>Ensures generated Android Gradle manifests declare Internet, network state, Wi-Fi state, and multicast permissions.</summary>
    internal sealed class DynamonsAndroidManifestPostProcessor : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        private const string AndroidNamespace = "http://schemas.android.com/apk/res/android";

        private static readonly string[] RequiredPermissions =
        {
            "android.permission.INTERNET",
            "android.permission.ACCESS_NETWORK_STATE",
            "android.permission.ACCESS_WIFI_STATE",
            "android.permission.CHANGE_WIFI_MULTICAST_STATE",
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            InjectPermissions(Path.Combine(path, "src/main/AndroidManifest.xml"));
            InjectPermissions(Path.Combine(path, "../launcher/src/main/AndroidManifest.xml"));
        }

        private static void InjectPermissions(string manifestPath)
        {
            if (!File.Exists(manifestPath)) return;

            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.Load(manifestPath);
            XmlElement root = doc.DocumentElement;
            if (root == null) return;

            string androidPrefix = root.GetPrefixOfNamespace(AndroidNamespace);
            if (string.IsNullOrEmpty(androidPrefix)) androidPrefix = "android";

            var existing = new HashSet<string>(StringComparer.Ordinal);
            XmlNode appNode = null;
            foreach (XmlNode node in root.ChildNodes)
            {
                if (!(node is XmlElement el)) continue;
                if (el.LocalName == "uses-permission")
                {
                    string name = el.GetAttribute("name", AndroidNamespace);
                    if (string.IsNullOrEmpty(name)) name = el.GetAttribute(androidPrefix + ":name");
                    if (!string.IsNullOrEmpty(name)) existing.Add(name);
                }
                else if (appNode == null && el.LocalName == "application")
                {
                    appNode = el;
                }
            }
            if (appNode == null) return;

            bool changed = false;
            foreach (string perm in RequiredPermissions)
            {
                if (existing.Contains(perm)) continue;
                XmlElement el = doc.CreateElement("uses-permission");
                XmlAttribute attr = doc.CreateAttribute(androidPrefix, "name", AndroidNamespace);
                attr.Value = perm;
                el.SetAttributeNode(attr);
                root.InsertBefore(el, appNode);
                root.InsertBefore(doc.CreateWhitespace("\n    "), appNode);
                changed = true;
            }

            if (changed) doc.Save(manifestPath);
        }
    }
#endif
}
