using UnityEditor;
using UnityEditor.Build;

namespace Dynamons
{
    /// <summary>Configures standalone player builds to compile with IL2CPP and minimal managed stripping.</summary>
    [InitializeOnLoad]
    internal static class DynamonsBuildSettings
    {
        static DynamonsBuildSettings()
        {
            if (PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone) != ScriptingImplementation.IL2CPP)
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
            if (PlayerSettings.GetManagedStrippingLevel(NamedBuildTarget.Standalone) != ManagedStrippingLevel.Minimal)
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Standalone, ManagedStrippingLevel.Minimal);
        }
    }
}
