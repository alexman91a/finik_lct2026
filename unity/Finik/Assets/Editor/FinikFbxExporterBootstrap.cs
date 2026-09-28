using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

[InitializeOnLoad]
public static class FinikFbxExporterBootstrap
{
    private const string PackageName = "com.unity.formats.fbx";
    private const string ResolveKey = "FinikFbxExporterResolveRequested";

    static FinikFbxExporterBootstrap()
    {
        EditorApplication.delayCall += EnsurePackage;
    }

    private static void EnsurePackage()
    {
        var info = UnityEditor.PackageManager.PackageInfo.FindForPackageName(PackageName);
        if (info != null)
        {
            Debug.Log($"FINIK_FBX_EXPORTER_READY version={info.version} source={info.source} path={info.resolvedPath}");
            return;
        }

        if (SessionState.GetBool(ResolveKey, false))
        {
            Debug.LogWarning("FINIK_FBX_EXPORTER_NOT_READY_AFTER_RESOLVE");
            return;
        }

        SessionState.SetBool(ResolveKey, true);
        Client.Resolve();
        Debug.Log("FINIK_FBX_EXPORTER_RESOLVE_REQUESTED");
    }
}
