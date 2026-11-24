using UnityEditor;
using UnityEditor.Build;

namespace Dynamons
{
    /// <summary>Configures Standalone and Android (ARM64) player builds with IL2CPP and minimal managed stripping.</summary>
    [InitializeOnLoad]
    internal static class DynamonsBuildSettings
    {
        static DynamonsBuildSettings()
        {
            if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = "Dynamons";
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName.Contains("dynamons-unity"))
                PlayerSettings.productName = "Dynamons 2";

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            ConfigureTarget(NamedBuildTarget.Standalone);
            ConfigureTarget(NamedBuildTarget.Android);

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
    }
}
