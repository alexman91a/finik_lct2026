using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace Finik.Editor
{
    public static class FinikStageShellSetup
    {
        const string ScenePath = "Assets/Finik/Scenes/FinikRoomNavigationPrototype.unity";
        const string MaterialFolder = "Assets/Finik/Materials";

        public static string ApplyFramingPass()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open FinikRoomNavigationPrototype before applying the framing pass.");
            var shell = GameObject.Find("RoomShell");
            if (!shell || !GameObject.Find("Floor"))
                throw new InvalidOperationException("The 10.5 x 5.8 m stage shell is missing.");
            if (shell.transform.Find("VISUAL_OVERSCAN"))
                throw new InvalidOperationException("VISUAL_OVERSCAN already exists; refusing to duplicate this pass.");

            var gameFloor = GameObject.Find("Floor").GetComponent<BoxCollider>();
            var cameraBounds = GameObject.Find("CameraBounds").GetComponent<BoxCollider>();
            var finik = GameObject.Find("Finik_Root");
            var nav = GameObject.Find("Navigation").GetComponent<NavMeshSurface>();
            Vector3 floorSize = gameFloor.bounds.size;
            Vector3 boundsSize = cameraBounds.bounds.size;
            Vector3 finikScale = finik.transform.localScale;
            int navTriangles = NavMesh.CalculateTriangulation().indices.Length / 3;
            if (Mathf.Abs(floorSize.x - 10.5f) > 0.01f || Mathf.Abs(floorSize.z - 5.8f) > 0.01f)
                throw new InvalidOperationException("Playable floor is not 10.5 x 5.8 m; refusing to alter unknown geometry.");

            Material wall = Material("Stage_Wall_Cream", new Color(0.82f, 0.76f, 0.67f), 0.12f);
            Material floor = Material("Stage_Floor_Warm", new Color(0.66f, 0.48f, 0.34f), 0.22f);
            Material trim = Material("Stage_Trim_White", new Color(0.93f, 0.87f, 0.79f), 0.18f);
            Material glass = Material("Stage_Window_Glass", new Color(0.53f, 0.71f, 0.77f), 0.6f);

            // Window centers +/-2.7 m. Two 1.9 m openings leave 3.5 m in the middle
            // and 1.6 m at each end of the 10.5 m wall.
            const float backZ = -5.8f;
            MovePiece("BackWallLeft", new Vector3(-4.45f, 1.55f, backZ), new Vector3(1.6f, 3.1f, 0.12f));
            MovePiece("BackWallRight", new Vector3(4.45f, 1.55f, backZ), new Vector3(1.6f, 3.1f, 0.12f));
            Cube(shell.transform, "BackWallCenter", new Vector3(0f, 1.55f, backZ), new Vector3(3.5f, 3.1f, 0.12f), wall);
            MovePiece("LeftWindowWallBottom", new Vector3(-2.7f, 0.45f, backZ), new Vector3(1.9f, 0.9f, 0.12f));
            MovePiece("LeftWindowWallTop", new Vector3(-2.7f, 2.85f, backZ), new Vector3(1.9f, 0.5f, 0.12f));
            Cube(shell.transform, "RightWindowWallBottom", new Vector3(2.7f, 0.45f, backZ), new Vector3(1.9f, 0.9f, 0.12f), wall);
            Cube(shell.transform, "RightWindowWallTop", new Vector3(2.7f, 2.85f, backZ), new Vector3(1.9f, 0.5f, 0.12f), wall);

            MovePiece("LeftWindowGlass", new Vector3(-2.7f, 1.75f, backZ + 0.02f), new Vector3(1.76f, 1.56f, 0.025f));
            MovePiece("LeftWindowFrameLeft", new Vector3(-3.65f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f));
            MovePiece("LeftWindowFrameRight", new Vector3(-1.75f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f));
            MovePiece("LeftWindowFrameTop", new Vector3(-2.7f, 2.6f, backZ + 0.08f), new Vector3(2.0f, 0.09f, 0.10f));
            MovePiece("LeftWindowFrameBottom", new Vector3(-2.7f, 0.9f, backZ + 0.08f), new Vector3(2.0f, 0.09f, 0.10f));
            MovePiece("LeftWindowSill", new Vector3(-2.7f, 0.87f, backZ + 0.22f), new Vector3(2.2f, 0.10f, 0.42f));
            Cube(shell.transform, "RightWindowGlass", new Vector3(2.7f, 1.75f, backZ + 0.02f), new Vector3(1.76f, 1.56f, 0.025f), glass, false);
            Cube(shell.transform, "RightWindowFrameLeft", new Vector3(1.75f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f), trim);
            Cube(shell.transform, "RightWindowFrameRight", new Vector3(3.65f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f), trim);
            Cube(shell.transform, "RightWindowFrameTop", new Vector3(2.7f, 2.6f, backZ + 0.08f), new Vector3(2.0f, 0.09f, 0.10f), trim);
            Cube(shell.transform, "RightWindowFrameBottom", new Vector3(2.7f, 0.9f, backZ + 0.08f), new Vector3(2.0f, 0.09f, 0.10f), trim);
            Cube(shell.transform, "RightWindowSill", new Vector3(2.7f, 0.87f, backZ + 0.22f), new Vector3(2.2f, 0.10f, 0.42f), trim);

            var overscan = new GameObject("VISUAL_OVERSCAN");
            Undo.RegisterCreatedObjectUndo(overscan, "Create visual-only room overscan");
            overscan.transform.SetParent(shell.transform, false);
            Cube(overscan.transform, "overscan_back_upper", new Vector3(0f, 3.75f, backZ), new Vector3(10.5f, 1.3f, 0.12f), wall, false);
            Cube(overscan.transform, "overscan_left_upper", new Vector3(-5.25f, 3.75f, -2.9f), new Vector3(0.12f, 1.3f, 5.8f), wall, false);
            Cube(overscan.transform, "overscan_right_upper", new Vector3(5.25f, 3.75f, -2.9f), new Vector3(0.12f, 1.3f, 5.8f), wall, false);
            Cube(overscan.transform, "overscan_floor_front", new Vector3(0f, -0.05f, 0.75f), new Vector3(10.5f, 0.1f, 1.5f), floor, false);
            Cube(overscan.transform, "overscan_left_front", new Vector3(-5.25f, 2.2f, 0.75f), new Vector3(0.12f, 4.4f, 1.5f), wall, false);
            Cube(overscan.transform, "overscan_right_front", new Vector3(5.25f, 2.2f, 0.75f), new Vector3(0.12f, 4.4f, 1.5f), wall, false);

            var camera = GameObject.Find("camera_gameplay").GetComponent<Camera>();
            var controller = camera.GetComponent<Navigation.FinikRoomCameraController>();
            if (!controller) throw new InvalidOperationException("FinikRoomCameraController is missing.");
            Undo.RecordObject(camera, "Tune landscape stage FOV");
            Undo.RecordObject(controller, "Tune landscape/portrait stage framing");
            camera.fieldOfView = 36f;
            var cameraProperties = new SerializedObject(controller);
            cameraProperties.FindProperty("landscapeFov").floatValue = 36f;
            cameraProperties.FindProperty("portraitFov").floatValue = 28.5f;
            cameraProperties.FindProperty("portraitDistance").floatValue = 4.25f;
            cameraProperties.ApplyModifiedProperties();

            var windowLight = GameObject.Find("light_window")?.GetComponent<Light>();
            if (windowLight) { Undo.RecordObject(windowLight.transform, "Align light with first window"); windowLight.transform.position = new Vector3(-2.7f, 2.25f, backZ + 0.28f); }

            if (gameFloor.bounds.size != floorSize || cameraBounds.bounds.size != boundsSize || finik.transform.localScale != finikScale)
                throw new InvalidOperationException("Playable floor, CameraBounds or Finik scale changed unexpectedly.");
            if (NavMesh.CalculateTriangulation().indices.Length / 3 != navTriangles || !nav.navMeshData)
                throw new InvalidOperationException("NavMesh changed; visual overscan must not alter gameplay navigation.");
            foreach (Transform child in overscan.transform)
                if (child.GetComponent<Collider>()) throw new InvalidOperationException("Visual overscan has a collider: " + child.name);

            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return $"Two 1.9x1.7 m windows at x=-2.7/+2.7; visual wall top=4.4 m, floor front=+1.5 m; gameplay floor={floorSize}; CameraBounds={boundsSize}; NavMesh triangles={navTriangles}; FOV landscape=36, portrait=28.5, portrait distance=4.25.";
        }

        static void MovePiece(string name, Vector3 position, Vector3 scale)
        {
            var gameObject = GameObject.Find(name);
            if (!gameObject) throw new InvalidOperationException("Stage shell piece missing: " + name);
            Undo.RecordObject(gameObject.transform, "Adjust two-window stage shell");
            gameObject.transform.localPosition = position;
            gameObject.transform.localScale = scale;
        }

        public static string Build()
        {
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != ScenePath)
                throw new InvalidOperationException("Open the canonical FinikRoomNavigationPrototype scene before building the stage shell.");
            if (GameObject.Find("RoomShell"))
                throw new InvalidOperationException("RoomShell already exists; refusing to replace or duplicate it automatically.");

            var finik = GameObject.Find("Finik_Root");
            var cameraObject = GameObject.Find("camera_gameplay");
            var navigation = GameObject.Find("Navigation")?.GetComponent<NavMeshSurface>();
            var boundsObject = GameObject.Find("CameraBounds");
            if (!finik || !cameraObject || !navigation || !boundsObject)
                throw new InvalidOperationException("Required Finik, camera, NavMeshSurface or CameraBounds object is missing.");
            Vector3 originalFinikPosition = finik.transform.position;
            Vector3 originalFinikScale = finik.transform.localScale;

            Material floorMaterial = Material("Stage_Floor_Warm", new Color(0.66f, 0.48f, 0.34f), 0.22f);
            Material wallMaterial = Material("Stage_Wall_Cream", new Color(0.82f, 0.76f, 0.67f), 0.12f);
            Material trimMaterial = Material("Stage_Trim_White", new Color(0.93f, 0.87f, 0.79f), 0.18f);
            Material glassMaterial = Material("Stage_Window_Glass", new Color(0.53f, 0.71f, 0.77f), 0.6f);

            var root = new GameObject("RoomShell");
            Undo.RegisterCreatedObjectUndo(root, "Create Finik stage shell");
            // Floor runs from z=0 (open front) to z=-5.8 (back wall).
            Cube(root.transform, "Floor", new Vector3(0f, -0.05f, -2.9f), new Vector3(10.5f, 0.1f, 5.8f), floorMaterial);
            Cube(root.transform, "WallLeft", new Vector3(-5.25f, 1.55f, -2.9f), new Vector3(0.12f, 3.1f, 5.8f), wallMaterial);
            Cube(root.transform, "WallRight", new Vector3(5.25f, 1.55f, -2.9f), new Vector3(0.12f, 3.1f, 5.8f), wallMaterial);

            // 2.4 x 1.7 m opening, slightly left of center, with sill at 0.9 m.
            const float windowX = -2.3f;
            const float backZ = -5.8f;
            Cube(root.transform, "BackWallLeft", new Vector3(-4.375f, 1.55f, backZ), new Vector3(1.75f, 3.1f, 0.12f), wallMaterial);
            Cube(root.transform, "BackWallRight", new Vector3(2.075f, 1.55f, backZ), new Vector3(6.35f, 3.1f, 0.12f), wallMaterial);
            Cube(root.transform, "LeftWindowWallBottom", new Vector3(windowX, 0.45f, backZ), new Vector3(2.4f, 0.9f, 0.12f), wallMaterial);
            Cube(root.transform, "LeftWindowWallTop", new Vector3(windowX, 2.85f, backZ), new Vector3(2.4f, 0.5f, 0.12f), wallMaterial);
            Cube(root.transform, "LeftWindowGlass", new Vector3(windowX, 1.75f, backZ + 0.02f), new Vector3(2.26f, 1.56f, 0.025f), glassMaterial, false);
            Cube(root.transform, "LeftWindowFrameLeft", new Vector3(-3.5f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f), trimMaterial);
            Cube(root.transform, "LeftWindowFrameRight", new Vector3(-1.1f, 1.75f, backZ + 0.08f), new Vector3(0.09f, 1.7f, 0.10f), trimMaterial);
            Cube(root.transform, "LeftWindowFrameTop", new Vector3(windowX, 2.6f, backZ + 0.08f), new Vector3(2.5f, 0.09f, 0.10f), trimMaterial);
            Cube(root.transform, "LeftWindowFrameBottom", new Vector3(windowX, 0.9f, backZ + 0.08f), new Vector3(2.5f, 0.09f, 0.10f), trimMaterial);
            Cube(root.transform, "LeftWindowSill", new Vector3(windowX, 0.87f, backZ + 0.22f), new Vector3(2.7f, 0.10f, 0.42f), trimMaterial);

            var hiddenShell = new List<string>();
            foreach (string name in new[] { "room_floor", "wall_back", "wall_left", "door_frame", "window_frame" })
            {
                var old = GameObject.Find(name);
                if (old) { Undo.RecordObject(old, "Hide old imported room shell"); old.SetActive(false); hiddenShell.Add(name); }
            }

            var box = boundsObject.GetComponent<BoxCollider>();
            if (!box) throw new InvalidOperationException("CameraBounds has no BoxCollider.");
            Undo.RecordObject(boundsObject.transform, "Resize camera bounds");
            Undo.RecordObject(box, "Resize camera bounds");
            boundsObject.transform.position = new Vector3(0f, 0f, -2.9f);
            box.size = new Vector3(10.5f, 2f, 5.8f);
            box.isTrigger = true;

            // Same Camera and same camera/input components; only the broad stage framing changes.
            var camera = cameraObject.GetComponent<Camera>();
            Undo.RecordObject(cameraObject.transform, "Frame wide Finik room");
            Undo.RecordObject(camera, "Frame wide Finik room");
            cameraObject.transform.position = new Vector3(0f, 5.1f, 8.2f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1.35f, -2.9f) - cameraObject.transform.position, Vector3.up);
            camera.fieldOfView = 38f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.46f, 0.42f, 0.39f);
            var controller = cameraObject.GetComponent<Navigation.FinikRoomCameraController>();
            if (controller)
            {
                Undo.RecordObject(controller, "Update Finik camera shell framing");
                var serialized = new SerializedObject(controller);
                serialized.FindProperty("landscapeFov").floatValue = 38f;
                serialized.FindProperty("portraitFov").floatValue = 38f;
                serialized.FindProperty("portraitDistance").floatValue = 5.5f;
                serialized.FindProperty("portraitPitchOffset").floatValue = 14f;
                serialized.ApplyModifiedProperties();
            }

            var windowLight = GameObject.Find("light_window")?.GetComponent<Light>();
            if (windowLight)
            {
                Undo.RecordObject(windowLight.transform, "Move window light");
                Undo.RecordObject(windowLight, "Tune window light");
                windowLight.transform.position = new Vector3(windowX, 2.25f, backZ + 0.28f);
                windowLight.transform.rotation = Quaternion.LookRotation(new Vector3(0.25f, -0.28f, 1f));
                windowLight.type = LightType.Spot;
                windowLight.range = 11f;
                windowLight.spotAngle = 95f;
                windowLight.intensity = 6f;
                windowLight.shadows = LightShadows.None;
                windowLight.color = new Color(1f, 0.84f, 0.68f);
            }
            var fill = GameObject.Find("light_fill")?.GetComponent<Light>();
            if (fill)
            {
                Undo.RecordObject(fill.transform, "Move ambient fill");
                Undo.RecordObject(fill, "Tune ambient fill");
                fill.transform.position = new Vector3(0f, 4.2f, -1.4f);
                fill.range = 12f;
                fill.intensity = 3.5f;
                fill.shadows = LightShadows.None;
            }
            var sun = GameObject.Find("SunFillLight")?.GetComponent<Light>();
            if (sun)
            {
                Undo.RecordObject(sun, "Tune daylight fill");
                sun.intensity = 1.15f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
            }
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.51f, 0.46f);

            navigation.BuildNavMesh();
            if (!navigation.navMeshData || NavMesh.CalculateTriangulation().indices.Length == 0)
                throw new InvalidOperationException("NavMesh rebake failed: no usable triangles were generated.");
            if (finik.transform.position != originalFinikPosition || finik.transform.localScale != originalFinikScale)
                throw new InvalidOperationException("Finik position or scale changed unexpectedly.");
            EditorUtility.SetDirty(navigation);
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            return $"Stage shell 10.5x5.8x3.1 built; window 2.4x1.7 at x={windowX}; hidden old shell={string.Join(",", hiddenShell)}; NavMesh triangles={NavMesh.CalculateTriangulation().indices.Length / 3}; Finik scale={originalFinikScale}.";
        }

        static Material Material(string name, Color color, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (!shader) throw new InvalidOperationException("No URP Lit or Standard shader available.");
            material = new Material(shader) { name = name, color = color };
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject Cube(Transform parent, string name, Vector3 position, Vector3 dimensions, Material material, bool collider = true)
        {
            var gameObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(gameObject, "Create stage shell piece");
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localScale = dimensions;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(gameObject.GetComponent<BoxCollider>());
            return gameObject;
        }
    }
}
