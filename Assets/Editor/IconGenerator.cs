using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using System.IO;

/// <summary>
/// Right-click a prefab in the Project window → "Generate Icon" to render a
/// transparent-background PNG icon and save it to Assets/Icons/.
/// Moves the instance far from scene geometry and disables post-processing
/// so the Global Volume and scene objects don't contaminate the render.
/// </summary>
public static class IconGenerator
{
    private const int IconSize = 512;
    private const string OutputFolder = "Assets/Icons";
    private static readonly Vector3 StudioOffset = new(0, 10000, 0); // far from any scene geometry

    [MenuItem("Assets/Generate Icon", false, 2000)]
    private static void GenerateIcon()
    {
        var prefab = Selection.activeGameObject;
        if (prefab == null) return;

        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);

        // Instantiate prefab far from scene geometry
        var instance = Object.Instantiate(prefab, StudioOffset, Quaternion.identity);
        instance.hideFlags = HideFlags.HideAndDontSave;

        // Strip components that error or interfere in editor
        foreach (var c in instance.GetComponentsInChildren<Unity.Netcode.NetworkBehaviour>())
            Object.DestroyImmediate(c);
        foreach (var c in instance.GetComponentsInChildren<Unity.Netcode.NetworkObject>())
            Object.DestroyImmediate(c);
        foreach (var c in instance.GetComponentsInChildren<Animator>())
            Object.DestroyImmediate(c);
        // Strip colliders (trigger colliders can have visible debug meshes)
        foreach (var c in instance.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        // Calculate bounds from all renderers
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[IconGenerator] No renderers found on {prefab.name}");
            Object.DestroyImmediate(instance);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        // Create temporary camera — disable post-processing so Global Volume doesn't affect it
        var camGO = new GameObject("_IconCamera") { hideFlags = HideFlags.HideAndDontSave };
        // 3/4 view: slightly from the right and above for a more interesting angle
        Vector3 viewDir = new Vector3(-0.3f, -0.25f, -1f).normalized;
        camGO.transform.position = bounds.center - viewDir * 10f;
        camGO.transform.LookAt(bounds.center);

        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0, 0, 0, 0);
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 100f;
        cam.cullingMask = ~0;

        // URP camera data: disable post-processing, anti-aliasing, and stop rendering
        var urpCam = camGO.AddComponent<UniversalAdditionalCameraData>();
        urpCam.renderPostProcessing = false;
        urpCam.antialiasing = AntialiasingMode.None;
        urpCam.renderShadows = false;

        // Frame the object
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.2f;
        if (maxExtent < 0.01f) maxExtent = 1f;
        cam.orthographicSize = maxExtent;

        // Create temporary directional light at the studio position
        var lightGO = new GameObject("_IconLight") { hideFlags = HideFlags.HideAndDontSave };
        lightGO.transform.position = StudioOffset;
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.intensity = 1.5f;

        // Render to texture
        var rt = new RenderTexture(IconSize, IconSize, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        // Read pixels
        var prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(IconSize, IconSize, TextureFormat.ARGB32, false);
        tex.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
        tex.Apply();
        RenderTexture.active = prevRT;

        // Save PNG
        string path = $"{OutputFolder}/{prefab.name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log($"[IconGenerator] Saved icon: {path}");

        // Cleanup
        Object.DestroyImmediate(instance);
        Object.DestroyImmediate(camGO);
        Object.DestroyImmediate(lightGO);
        Object.DestroyImmediate(tex);
        rt.Release();
        Object.DestroyImmediate(rt);

        // Import as Sprite
        AssetDatabase.Refresh();
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
    }

    [MenuItem("Assets/Generate Icon", true)]
    private static bool ValidateGenerateIcon()
    {
        var obj = Selection.activeGameObject;
        return obj != null && PrefabUtility.IsPartOfPrefabAsset(obj);
    }
}
