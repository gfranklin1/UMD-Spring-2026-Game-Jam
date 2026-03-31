using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// URP 17 Renderer Feature that applies screen-space underwater distance fog.
///
/// Setup:
///   1. Add this feature to Assets/Settings/PC_Renderer (Add Renderer Feature → UnderwaterFogFeature).
///   2. Drag Assets/Materials/UnderwaterFogMaterial into the "Fog Material" slot.
///   3. UnderwaterEffect.cs drives the effect each frame via Shader.SetGlobal* properties.
/// </summary>
public class UnderwaterFogFeature : ScriptableRendererFeature
{
    [Tooltip("Material using Hidden/UnderwaterFog shader.")]
    public Material fogMaterial;

    private UnderwaterFogPass _pass;

    public override void Create()
    {
        _pass = new UnderwaterFogPass(fogMaterial)
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (fogMaterial == null) return;
        // Only run for Game and SceneView cameras
        var camType = renderingData.cameraData.cameraType;
        if (camType != CameraType.Game && camType != CameraType.SceneView) return;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }

    // ─── Inner pass ───────────────────────────────────────────────────────────

    private class UnderwaterFogPass : ScriptableRenderPass
    {
        private readonly Material _material;

        private static readonly int s_FogDensityId = Shader.PropertyToID("_UnderwaterFogDensity");

        public UnderwaterFogPass(Material material)
        {
            _material = material;
        }

        // ── Render-graph path (URP 17 / Unity 6) ──────────────────────────────

        private class PassData
        {
            public TextureHandle source;
            public Material      material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            // Skip only when fog density is zero — the shader self-governs via water column length
            if (Shader.GetGlobalFloat(s_FogDensityId) < 0.0001f) return;

            var resourceData = frameData.Get<UniversalResourceData>();

            // Can't blit when rendering directly to back buffer
            if (resourceData.isActiveTargetBackBuffer) return;

            TextureHandle src = resourceData.activeColorTexture;

            // Create a temp texture matching the camera color
            var desc = renderGraph.GetTextureDesc(src);
            desc.name        = "_UnderwaterFogTemp";
            desc.clearBuffer = false;
            TextureHandle dst = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("UnderwaterFog", out var data))
            {
                data.source   = src;
                data.material = _material;

                builder.UseTexture(data.source, AccessFlags.Read);
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc(static (PassData d, RasterGraphContext ctx) =>
                    Blitter.BlitTexture(ctx.cmd, d.source,
                                        new Vector4(1f, 1f, 0f, 0f), d.material, 0));
            }

            // Replace the camera colour with our fogged output
            resourceData.cameraColor = dst;
        }

        public void Dispose() { /* nothing to release — no RTHandle allocations */ }
    }
}
