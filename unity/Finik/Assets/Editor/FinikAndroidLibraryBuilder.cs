using System;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class FinikAndroidLibraryBuilder
{
    private const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";

    [MenuItem("Finik/Build/Export Android Library")]
    public static void ExportFromMenu()
    {
        Export();
        EditorUtility.RevealInFinder(GetOutputPath());
    }

    public static void ExportFromCommandLine()
    {
        try
        {
            Export();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
            throw;
        }
    }

    private static void Export()
    {
        if (!File.Exists(ScenePath))
        {
            throw new FileNotFoundException($"Unity scene was not found: {ScenePath}");
        }

        var outputPath = GetOutputPath();
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }
        Directory.CreateDirectory(outputPath);

        var previousExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        var previousArchitectures = PlayerSettings.Android.targetArchitectures;
        var previousBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android);

        try
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);

            // Include ARM64 for real devices and x86_64 for the Android emulator used in development.
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.X86_64;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.AcceptExternalModificationsToPlayer
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new Exception(
                    $"Unity Android export failed: {report.summary.result}. " +
                    $"Errors: {report.summary.totalErrors}, warnings: {report.summary.totalWarnings}."
                );
            }

            RemoveLauncherIntentFilter(outputPath);
            PatchMissingIl2CppSymbols(outputPath);

            var unityLibrary = Path.Combine(outputPath, "unityLibrary", "build.gradle");
            if (!File.Exists(unityLibrary))
            {
                throw new FileNotFoundException(
                    "Unity export finished, but unityLibrary/build.gradle was not generated.",
                    unityLibrary
                );
            }

            Debug.Log($"Finik Android Unity library exported to: {outputPath}");
        }
        finally
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportProject;
            PlayerSettings.Android.targetArchitectures = previousArchitectures;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, previousBackend);
        }
    }

    private static string GetOutputPath()
    {
        var unityProject = Directory.GetParent(Application.dataPath)
            ?? throw new DirectoryNotFoundException("Could not resolve Unity project root.");
        var unityRoot = unityProject.Parent
            ?? throw new DirectoryNotFoundException("Could not resolve repository unity directory.");
        var repositoryRoot = unityRoot.Parent
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");

        return Path.GetFullPath(Path.Combine(repositoryRoot.FullName, "unity", "builds", "android"));
    }

    private static void PatchMissingIl2CppSymbols(string outputPath)
    {
        var gradlePath = Path.Combine(outputPath, "unityLibrary", "build.gradle");
        if (!File.Exists(gradlePath))
        {
            throw new FileNotFoundException("Unity library build.gradle was not generated.", gradlePath);
        }

        var gradle = File.ReadAllText(gradlePath);
        const string original = "    ant.move(file: \"${workingDir}/src/main/jniLibs/${abi}/libil2cpp${extensionToKeep}\", tofile: \"${workingDir}/symbols/${abi}/libil2cpp.so\")";
        const string replacement =
            "    def symbolFile = file(\"${workingDir}/src/main/jniLibs/${abi}/libil2cpp${extensionToKeep}\")\n" +
            "    if (symbolFile.exists()) {\n" +
            "        ant.move(\n" +
            "            file: symbolFile,\n" +
            "            tofile: \"${workingDir}/symbols/${abi}/libil2cpp.so\"\n" +
            "        )\n" +
            "    } else {\n" +
            "        println \"Unity: IL2CPP symbol file not generated for ${abi}; skipping symbol copy.\"\n" +
            "    }";

        if (gradle.Contains(replacement))
        {
            return;
        }

        if (!gradle.Contains(original))
        {
            throw new InvalidOperationException(
                "Unity generated an unexpected unityLibrary/build.gradle template. " +
                "Could not apply the IL2CPP symbol-file safeguard."
            );
        }

        File.WriteAllText(gradlePath, gradle.Replace(original, replacement));
    }

    private static void RemoveLauncherIntentFilter(string outputPath)
    {
        var manifestPath = Path.Combine(
            outputPath,
            "unityLibrary",
            "src",
            "main",
            "AndroidManifest.xml"
        );

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Unity library AndroidManifest.xml was not generated.", manifestPath);
        }

        var document = new XmlDocument();
        document.PreserveWhitespace = true;
        document.Load(manifestPath);

        var namespaceManager = new XmlNamespaceManager(document.NameTable);
        namespaceManager.AddNamespace("android", "http://schemas.android.com/apk/res/android");

        var filters = document.SelectNodes("//intent-filter");
        if (filters == null) return;

        foreach (XmlNode filter in filters)
        {
            var mainAction = filter.SelectSingleNode(
                "action[@android:name='android.intent.action.MAIN']",
                namespaceManager
            );
            var launcherCategory = filter.SelectSingleNode(
                "category[@android:name='android.intent.category.LAUNCHER']",
                namespaceManager
            );

            if ((mainAction != null || launcherCategory != null) && filter.ParentNode != null)
            {
                filter.ParentNode.RemoveChild(filter);
            }
        }

        document.Save(manifestPath);
    }
}
