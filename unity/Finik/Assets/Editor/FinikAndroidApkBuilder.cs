using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds a standalone, installable APK of the Unity game (unlike FinikAndroidLibraryBuilder, which
/// exports a library for the React Native shell). ARM64 + IL2CPP, signed with the debug key. Writes
/// build_report.txt next to the APK: the heaviest assets and totals per asset type, from the BuildReport.
/// Player settings touched for the build are restored afterwards.
/// </summary>
public static class FinikAndroidApkBuilder
{
    private const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
    private const string ApplicationId = "com.finik.game";
    private const int TopAssets = 60;

    [MenuItem("Finik/Build/Build Android APK")]
    public static void BuildFromMenu()
    {
        string apk = Build();
        EditorUtility.RevealInFinder(apk);
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
            throw;
        }
    }

    public static string Build()
    {
        if (!File.Exists(ScenePath)) throw new FileNotFoundException($"Unity scene was not found: {ScenePath}");

        string outputDir = GetOutputDirectory();
        Directory.CreateDirectory(outputDir);
        string apkPath = Path.Combine(outputDir, "Finik.apk");
        if (File.Exists(apkPath)) File.Delete(apkPath);

        var android = NamedBuildTarget.Android;
        var previousExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        var previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        var previousArchitectures = PlayerSettings.Android.targetArchitectures;
        var previousBackend = PlayerSettings.GetScriptingBackend(android);
        var previousId = PlayerSettings.GetApplicationIdentifier(android);
        var previousDefaultGraphicsApis = PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android);
        var previousGraphicsApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);

        try
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            // Real phones only: x86_64 (emulators) would roughly double the native code in the APK.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetApplicationIdentifier(android, ApplicationId);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });
            FinikAndroidIconSetup.Apply();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = apkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new Exception($"Android APK build failed: {report.summary.result}. " +
                                    $"Errors: {report.summary.totalErrors}, warnings: {report.summary.totalWarnings}.");

            string reportPath = Path.Combine(outputDir, "build_report.txt");
            File.WriteAllText(reportPath, Describe(report, apkPath), Encoding.UTF8);
            Debug.Log($"[FinikApk] Built {apkPath} ({new FileInfo(apkPath).Length / 1048576f:0.0} MB). Report: {reportPath}");
            return apkPath;
        }
        finally
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportProject;
            EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
            PlayerSettings.Android.targetArchitectures = previousArchitectures;
            PlayerSettings.SetScriptingBackend(android, previousBackend);
            PlayerSettings.SetApplicationIdentifier(android, previousId);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, previousGraphicsApis);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, previousDefaultGraphicsApis);
        }
    }

    /// <summary>APK size, the heaviest assets as packed into the build, and totals per asset type.</summary>
    private static string Describe(BuildReport report, string apkPath)
    {
        var entries = report.packedAssets
            .SelectMany(pack => pack.contents)
            .GroupBy(content => content.sourceAssetPath)
            .Select(group => (path: string.IsNullOrEmpty(group.Key) ? "(built-in / generated)" : group.Key,
                type: group.First().type?.Name ?? "?",
                size: group.Sum(content => (long)content.packedSize)))
            .OrderByDescending(entry => entry.size)
            .ToList();

        var text = new StringBuilder();
        text.AppendLine($"APK: {apkPath}");
        text.AppendLine($"APK size: {Mb(new FileInfo(apkPath).Length)}");
        text.AppendLine($"Packed assets total (uncompressed, before APK zip): {Mb(entries.Sum(e => e.size))}");
        text.AppendLine();
        text.AppendLine("By type:");
        foreach (var type in entries.GroupBy(e => e.type).Select(g => (g.Key, size: g.Sum(e => e.size), count: g.Count()))
                     .OrderByDescending(t => t.size))
            text.AppendLine($"  {Mb(type.size),10}  {type.count,5}  {type.Key}");
        text.AppendLine();
        text.AppendLine($"Top {TopAssets} assets:");
        foreach (var entry in entries.Take(TopAssets))
            text.AppendLine($"  {Mb(entry.size),10}  {entry.type,-18} {entry.path}");
        return text.ToString();
    }

    private static string Mb(long bytes) => $"{bytes / 1048576d:0.00} MB";

    private static string GetOutputDirectory()
    {
        var unityProject = Directory.GetParent(Application.dataPath)
            ?? throw new DirectoryNotFoundException("Could not resolve Unity project root.");
        var unityRoot = unityProject.Parent
            ?? throw new DirectoryNotFoundException("Could not resolve repository unity directory.");
        return Path.Combine(unityRoot.FullName, "builds", "android-apk");
    }
}
