using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

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
            if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "Dynamons";
            if (string.IsNullOrEmpty(PlayerSettings.productName) ||
                PlayerSettings.productName.Contains("dynamons-unity") ||
                PlayerSettings.productName == "Dynamons 2")
                PlayerSettings.productName = "Dynamons Next";

            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            ConfigureTarget(NamedBuildTarget.Standalone);
            ConfigureTarget(NamedBuildTarget.Android);
            ConfigureIcons();

            string appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrEmpty(appId) || appId.Contains("-"))
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.dynamons.game");
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion))
                PlayerSettings.bundleVersion = "1.0.0";
            if (PlayerSettings.Android.bundleVersionCode < 1)
                PlayerSettings.Android.bundleVersionCode = 1;
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
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
}
