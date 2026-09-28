#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Finik.EditorTools
{
    /// <summary>
    /// Installs Samsung Galaxy Z Fold6 fixed-resolution Game View presets.
    /// Uses UnityEditor internal GameViewSizes API so the presets appear in the
    /// standard Game View resolution dropdown without requiring manual setup.
    /// </summary>
    [InitializeOnLoad]
    public static class FinikGalaxyFoldGameViewPresets
    {
        private const string MenuPath = "Finik/Testing/Install Galaxy Z Fold6 Game View Presets";

        private readonly struct Preset
        {
            public readonly string Name;
            public readonly int Width;
            public readonly int Height;

            public Preset(string name, int width, int height)
            {
                Name = name;
                Width = width;
                Height = height;
            }
        }

        private static readonly Preset[] Presets =
        {
            new("Galaxy Z Fold6 Main Portrait", 1856, 2160),
            new("Galaxy Z Fold6 Main Landscape", 2160, 1856),
            new("Galaxy Z Fold6 Cover Portrait", 968, 2376),
            new("Galaxy Z Fold6 Cover Landscape", 2376, 968),
        };

        static FinikGalaxyFoldGameViewPresets()
        {
            // Delay until the editor is fully initialized. The operation is idempotent.
            EditorApplication.delayCall += EnsurePresetsOnDelay;
        }

        private static void EnsurePresetsOnDelay()
        {
            EnsurePresets();
        }

        [MenuItem(MenuPath)]
        public static void InstallFromMenu()
        {
            int added = EnsurePresets();
            Debug.Log(
                added == 0
                    ? "FINIK_FOLD6_PRESETS: Galaxy Z Fold6 Game View presets already installed."
                    : $"FINIK_FOLD6_PRESETS: installed {added} Galaxy Z Fold6 Game View preset(s).");
        }

        /// <summary>
        /// Can also be invoked via -executeMethod for validation/CI-style setup.
        /// </summary>
        public static int EnsurePresets()
        {
            try
            {
                Assembly editorAssembly = typeof(UnityEditor.Editor).Assembly;
                Type gameViewSizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
                Type gameViewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
                Type gameViewSizeTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeType");
                Type groupTypeEnum = editorAssembly.GetType("UnityEditor.GameViewSizeGroupType");

                if (gameViewSizesType == null || gameViewSizeType == null ||
                    gameViewSizeTypeEnum == null || groupTypeEnum == null)
                {
                    Debug.LogWarning("FINIK_FOLD6_PRESETS: Unity Game View API types not found.");
                    return 0;
                }

                Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
                object sizesInstance = singletonType
                    .GetProperty("instance", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null);

                if (sizesInstance == null)
                {
                    Debug.LogWarning("FINIK_FOLD6_PRESETS: GameViewSizes singleton unavailable.");
                    return 0;
                }

                MethodInfo getGroup = gameViewSizesType.GetMethod(
                    "GetGroup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                ConstructorInfo ctor = gameViewSizeType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { gameViewSizeTypeEnum, typeof(int), typeof(int), typeof(string) },
                    null);

                if (ctor == null)
                {
                    Debug.LogWarning("FINIK_FOLD6_PRESETS: Game View size constructor unavailable.");
                    return 0;
                }

                object fixedResolution = Enum.Parse(gameViewSizeTypeEnum, "FixedResolution");
                int added = 0;

                // Install for both editor desktop testing and the actual Android build target.
                foreach (string groupName in new[] { "Standalone", "Android" })
                {
                    object groupValue = Enum.Parse(groupTypeEnum, groupName);
                    object group = getGroup?.Invoke(sizesInstance, new[] { groupValue });

                    if (group == null)
                    {
                        Debug.LogWarning($"FINIK_FOLD6_PRESETS: {groupName} Game View group unavailable.");
                        continue;
                    }

                    Type groupType = group.GetType();
                    MethodInfo getTotalCount = groupType.GetMethod(
                        "GetTotalCount",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    MethodInfo getGameViewSize = groupType.GetMethod(
                        "GetGameViewSize",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    MethodInfo addCustomSize = groupType.GetMethod(
                        "AddCustomSize",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    if (getTotalCount == null || getGameViewSize == null || addCustomSize == null)
                    {
                        Debug.LogWarning($"FINIK_FOLD6_PRESETS: Game View reflection API unavailable for {groupName}.");
                        continue;
                    }

                    foreach (Preset preset in Presets)
                    {
                        if (ContainsResolution(group, getTotalCount, getGameViewSize, preset.Width, preset.Height))
                            continue;

                        object size = ctor.Invoke(new object[]
                        {
                            fixedResolution,
                            preset.Width,
                            preset.Height,
                            preset.Name
                        });

                        addCustomSize.Invoke(group, new[] { size });
                        added++;
                    }
                }

                if (added > 0)
                {
                    // Persist immediately when this Unity version exposes SaveToHDD.
                    gameViewSizesType.GetMethod(
                            "SaveToHDD",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?.Invoke(sizesInstance, null);

                    Debug.Log($"FINIK_FOLD6_PRESETS: installed {added} preset(s).");
                }

                return added;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"FINIK_FOLD6_PRESETS: could not install presets: {e.Message}");
                return 0;
            }
        }

        private static bool ContainsResolution(
            object group,
            MethodInfo getTotalCount,
            MethodInfo getGameViewSize,
            int width,
            int height)
        {
            int count = (int)getTotalCount.Invoke(group, null);
            for (int i = 0; i < count; i++)
            {
                object size = getGameViewSize.Invoke(group, new object[] { i });
                if (size == null)
                    continue;

                int existingWidth = ReadInt(size, "width");
                int existingHeight = ReadInt(size, "height");

                if (existingWidth == width && existingHeight == height)
                    return true;
            }

            return false;
        }

        private static int ReadInt(object instance, string memberName)
        {
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(int))
                return (int)property.GetValue(instance);

            FieldInfo field = type.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(int))
                return (int)field.GetValue(instance);

            return -1;
        }
    }
}
#endif
