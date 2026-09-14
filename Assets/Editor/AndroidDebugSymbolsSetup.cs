using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>Keep crash diagnostics usable when building Android players.</summary>
[InitializeOnLoad]
public sealed class AndroidDebugSymbolsSetup : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;
    static AndroidDebugSymbolsSetup() { EditorApplication.delayCall += EnsureSymbols; }
    private static void EnsureSymbols()
    {
        if (UnityEditor.Android.UserBuildSettings.DebugSymbols.level == Unity.Android.Types.DebugSymbolLevel.None)
            UnityEditor.Android.UserBuildSettings.DebugSymbols.level = Unity.Android.Types.DebugSymbolLevel.SymbolTable;
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.Android) EnsureSymbols();
    }
}
