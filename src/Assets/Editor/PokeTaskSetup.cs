using System.Collections.Generic;
using Leap.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VirtualShowcase.Showcase;

/// <summary>
///     One-click scene setup for the poke task (Tools → Poke Task → Create Setup In Scene).
///     Builds the task field: a textured dirt ground with tall-grass clumps standing on it, lit by its own
///     slanted light, tilted so that it lies flat in the real world (the display is tilted 45 degrees),
///     plus the PokeTask object with all references wired. If a setup already exists, it asks before
///     rebuilding it. Everything can be undone with Ctrl+Z.
/// </summary>
public static class PokeTaskSetup
{
    private const string RootName = "PokeTask";
    private const string FieldName = "PokeField";
    private const string OldPatchesName = "GrassPatches"; // from the first version of this setup

    // The virtual image plane is tilted 45 degrees, so the ground that lies flat in the real world
    // is the scene's XZ plane rotated by this angle around X. Change the PokeField's rotation X if the
    // ground does not look flat on the real device (e.g. -90 = looking straight down onto the field).
    private const float FieldTiltDegrees = -45f;

    // Ground size (cm) and its center in the field's own space (X across, Z away from the viewer).
    // The decorator fades its rim to black, so the ground is not a hard-edged slab.
    private static readonly Vector2 GroundSize = new Vector2(64f, 44f);
    private static readonly Vector2 GroundCenter = new Vector2(0f, -1f);

    // Clump positions on the ground (X, Z), field space. Back row and front row; a frog landing in front
    // of a back-row clump has more than 8 cm to the front row. Blade height is kept low enough that the
    // tallest back-row blades stay inside the screen (30 cm tall) when the field is tilted.
    private static readonly Vector2[] ClumpPositions =
    {
        new Vector2(-16f, 10f), new Vector2(0f, 10f), new Vector2(16f, 10f),
        new Vector2(-16f, -5f), new Vector2(0f, -5f), new Vector2(16f, -5f)
    };

    // Initial hop direction of every clump (same order as ClumpPositions): angle around the ground's up axis
    // (0 = toward the viewer, +90 = to the viewer's left, -90 = to the viewer's right) and distance in cm.
    // Chosen so that every landing spot is on bare ground, well away from the other clumps.
    // Change them later per clump: Inspector (Hop Angle Degrees / Hop Distance) or drag the yellow handle
    // in the Scene view.
    private static readonly Vector2[] HopDirections =
    {
        new Vector2(-35f, 9f), new Vector2(20f, 9f), new Vector2(35f, 9f),
        new Vector2(-25f, 9f), new Vector2(0f, 9f), new Vector2(25f, 9f)
    };

    private const string TextureFolder = "Assets/Textures/PokeGround";
    private const string GroundMaterialPath = "Assets/Materials/PokeGround.mat";

    [MenuItem("Tools/Poke Task/Create Setup In Scene")]
    public static void CreateSetup()
    {
        var frogTouch = Object.FindObjectOfType<FrogTouchController>();
        var vocalizer = Object.FindObjectOfType<FrogVocalizer>();
        var leapProvider = Object.FindObjectOfType<LeapProvider>();

        if (frogTouch == null)
        {
            Debug.LogError("[PokeTaskSetup] No FrogTouchController in the open scene. Open FrogRoom first.");
            return;
        }

        GameObject existingRoot = GameObject.Find(RootName);
        GameObject existingField = GameObject.Find(FieldName);
        GameObject existingOldPatches = GameObject.Find(OldPatchesName);
        bool exists = existingRoot != null || existingField != null || existingOldPatches != null;

        // Hop directions that were adjusted by hand survive a rebuild (matched by clump name).
        var keptHops = new Dictionary<string, Vector2>();

        if (exists)
        {
            bool rebuild = EditorUtility.DisplayDialog(
                "Poke Task",
                "既存の PokeTask / PokeField / GrassPatches を削除して作り直します。\n" +
                "・草むらごとの飛び出し方向(角度・距離)は引き継ぎます\n" +
                "・それ以外の手で調整した値は失われます(Ctrl+Zで戻せます)",
                "作り直す",
                "キャンセル");
            if (!rebuild)
            {
                return;
            }

            foreach (GrassPatch oldPatch in Object.FindObjectsOfType<GrassPatch>(true))
            {
                var oldSerialized = new SerializedObject(oldPatch);
                SerializedProperty angle = oldSerialized.FindProperty("hopAngleDegrees");
                SerializedProperty distance = oldSerialized.FindProperty("hopDistance");
                if (angle != null && distance != null)
                {
                    keptHops[oldPatch.name] = new Vector2(angle.floatValue, distance.floatValue);
                }
            }

            DestroyIfExists(existingRoot);
            DestroyIfExists(existingField);
            DestroyIfExists(existingOldPatches);
        }

        // Field: everything on the ground lives under this object, so the whole field can be
        // tilted (or hidden) in one place.
        var field = new GameObject(FieldName);
        Undo.RegisterCreatedObjectUndo(field, "Create Poke Task Setup");
        field.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(FieldTiltDegrees, 0f, 0f));

        CreateGround(field.transform);
        CreateLight(field.transform);

        var patches = new List<GrassPatch>();
        for (var i = 0; i < ClumpPositions.Length; i++)
        {
            var patchObject = new GameObject($"GrassPatch_{i + 1}");
            patchObject.transform.SetParent(field.transform, false);
            patchObject.transform.localPosition = new Vector3(ClumpPositions[i].x, 0f, ClumpPositions[i].y);

            var patch = patchObject.AddComponent<GrassPatch>();
            var patchSerialized = new SerializedObject(patch);
            patchSerialized.FindProperty("bladeSeed").intValue = i + 1;
            Vector2 hop = keptHops.TryGetValue(patchObject.name, out Vector2 kept) ? kept : HopDirections[i];
            patchSerialized.FindProperty("hopAngleDegrees").floatValue = hop.x;
            patchSerialized.FindProperty("hopDistance").floatValue = hop.y;
            patchSerialized.ApplyModifiedPropertiesWithoutUndo();

            patches.Add(patch);
        }

        // Pebbles, tufts and the fade to black: generated at runtime the first time the field is shown.
        var decorator = field.AddComponent<PokeFieldDecorator>();
        var decoratorSerialized = new SerializedObject(decorator);
        decoratorSerialized.FindProperty("areaCenter").vector2Value = GroundCenter;
        decoratorSerialized.FindProperty("areaSize").vector2Value = GroundSize;
        decoratorSerialized.ApplyModifiedPropertiesWithoutUndo();

        // Manager (outside of the field, which is switched off while no task is running).
        var rootObject = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(rootObject, "Create Poke Task Setup");
        var manager = rootObject.AddComponent<PokeTaskManager>();

        var managerSerialized = new SerializedObject(manager);
        SerializedProperty patchesProperty = managerSerialized.FindProperty("patches");
        patchesProperty.arraySize = patches.Count;
        for (var i = 0; i < patches.Count; i++)
        {
            patchesProperty.GetArrayElementAtIndex(i).objectReferenceValue = patches[i];
        }

        managerSerialized.FindProperty("fieldRoot").objectReferenceValue = field;
        managerSerialized.FindProperty("frogTouch").objectReferenceValue = frogTouch;
        managerSerialized.FindProperty("vocalizer").objectReferenceValue = vocalizer;
        managerSerialized.FindProperty("leapProvider").objectReferenceValue = leapProvider;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(rootObject.scene);
        Selection.activeGameObject = rootObject;

        Debug.Log($"[PokeTaskSetup] Created '{FieldName}' (tilt {FieldTiltDegrees} deg) with {patches.Count} clumps, and '{RootName}'. " +
                  $"Vocalizer={(vocalizer != null ? "ok" : "MISSING")}, " +
                  $"LeapProvider={(leapProvider != null ? "ok" : "MISSING (assign it by hand)")}. " +
                  $"Kept hop settings of {keptHops.Count} clumps. " +
                  "Pebbles, tufts and the black fade appear in Play. The field is only visible while a task runs. " +
                  "Save the scene (Ctrl+S). Hop directions: Tools > Poke Task > Edit Hop Directions. " +
                  "Experimenter monitor: Tools > Poke Task > Monitor.");
    }

    private static void CreateGround(UnityEngine.Transform parent)
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";

        // No collider: it would block the finger raycast used for touching the frog.
        Object.DestroyImmediate(ground.GetComponent<Collider>());

        ground.transform.SetParent(parent, false);
        ground.transform.localPosition = new Vector3(GroundCenter.x, 0f, GroundCenter.y);
        // A Unity plane primitive is 10 x 10 units.
        ground.transform.localScale = new Vector3(GroundSize.x / 10f, 1f, GroundSize.y / 10f);

        var groundRenderer = ground.GetComponent<MeshRenderer>();
        groundRenderer.sharedMaterial = ConfigureGroundMaterial();
        groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        groundRenderer.receiveShadows = true;
    }

    /// <summary>
    ///     A slanted, warm directional light that belongs to the field (it is only on while the field is shown).
    ///     It is brighter than the scene's own front light, so it becomes the main light that casts the
    ///     shadows of the frog and the grass onto the ground.
    /// </summary>
    private static void CreateLight(UnityEngine.Transform parent)
    {
        var lightObject = new GameObject("PokeField Light");
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localRotation = Quaternion.Euler(55f, 35f, 0f);

        var fieldLight = lightObject.AddComponent<Light>();
        fieldLight.type = LightType.Directional;
        fieldLight.color = new Color(1f, 0.95f, 0.85f);
        fieldLight.intensity = 1.2f;
        fieldLight.shadows = LightShadows.Soft;
        fieldLight.shadowStrength = 0.85f;
    }

    /// <summary>
    ///     Creates or updates Assets/Materials/PokeGround.mat as a lit dirt material (Poly Haven "Brown Mud",
    ///     CC0). Falls back to a plain brown colour if the textures are not in the project.
    /// </summary>
    private static Material ConfigureGroundMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
        var isNew = material == null;
        if (isNew)
        {
            material = new Material(shader) { name = "PokeGround" };
        }
        else
        {
            material.shader = shader;
        }

        var diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/brown_mud_diff_1k.jpg");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureFolder}/brown_mud_nor_gl_1k.jpg");

        var tint = new Color(0.9f, 0.85f, 0.8f);
        material.color = diffuse != null ? tint : new Color(0.42f, 0.27f, 0.15f);
        SetColorIfPresent(material, "_BaseColor", material.color);

        if (diffuse != null)
        {
            material.mainTexture = diffuse;
            SetTextureIfPresent(material, "_BaseMap", diffuse);
        }

        if (normal != null)
        {
            EnsureNormalMap($"{TextureFolder}/brown_mud_nor_gl_1k.jpg");
            SetTextureIfPresent(material, "_BumpMap", normal);
            material.EnableKeyword("_NORMALMAP");
            SetFloatIfPresent(material, "_BumpScale", 1f);
        }

        // Tiling: the 1k texture repeats 7 x 5 times over the 64 x 44 cm ground.
        material.mainTextureScale = new Vector2(7f, 5f);
        if (material.HasProperty("_BaseMap"))
        {
            material.SetTextureScale("_BaseMap", new Vector2(7f, 5f));
        }

        SetFloatIfPresent(material, "_Smoothness", 0.12f);
        SetFloatIfPresent(material, "_Metallic", 0f);

        if (isNew)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            {
                AssetDatabase.CreateFolder("Assets", "Materials");
            }

            AssetDatabase.CreateAsset(material, GroundMaterialPath);
        }
        else
        {
            EditorUtility.SetDirty(material);
        }

        AssetDatabase.SaveAssets();

        if (diffuse == null)
        {
            Debug.LogWarning($"[PokeTaskSetup] Ground texture not found in {TextureFolder}; using a plain brown colour.");
        }

        return material;
    }

    /// <summary>The normal map must be imported as a normal map, otherwise it looks wrong.</summary>
    private static void EnsureNormalMap(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
    }

    private static void SetColorIfPresent(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
        {
            material.SetColor(property, color);
        }
    }

    private static void SetTextureIfPresent(Material material, string property, Texture texture)
    {
        if (material.HasProperty(property))
        {
            material.SetTexture(property, texture);
        }
    }

    private static void SetFloatIfPresent(Material material, string property, float value)
    {
        if (material.HasProperty(property))
        {
            material.SetFloat(property, value);
        }
    }

    private static void DestroyIfExists(GameObject target)
    {
        if (target != null)
        {
            Undo.DestroyObjectImmediate(target);
        }
    }
}
