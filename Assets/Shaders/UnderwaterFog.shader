// Fullscreen underwater distance fog using the depth buffer.
// Works both when the camera is ABOVE water (looking in) and BELOW water (looking around).
// For each pixel, computes how far the camera ray travels THROUGH the water volume,
// then applies exponential fog based on that water-column length.
//
// Above water: ray enters at water surface plane, exits at scene geometry.
// Below water: ray starts at camera, exits at scene geometry or water surface (looking up).
//
// Driven per-frame by UnderwaterEffect.cs via Shader.SetGlobal*.
Shader "Hidden/UnderwaterFog"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "UnderwaterFog"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // Set every frame by UnderwaterEffect.cs via Shader.SetGlobal*
            float4 _UnderwaterFogColor;     // fog colour to blend toward
            float  _UnderwaterFogDensity;   // exponential coefficient (higher = shorter range)
            float  _UnderwaterFogOffset;    // metres before fog starts (skip close geometry)
            float  _UnderwaterFogWeight;    // 0-1 master blend for smooth surface-crossing transition
            float  _WaterSurfaceY;          // world-space Y of water plane

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float rawDepth = SampleSceneDepth(uv);

                // Reconstruct world-space position of the scene pixel
                float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float3 camPos  = _WorldSpaceCameraPos;
                float3 rayVec  = worldPos - camPos;
                float  tScene  = length(rayVec);              // distance to scene geometry
                float3 rayDir  = rayVec / max(tScene, 0.0001);

                // ── Compute how far this ray travels through the water volume ──────────
                float waterLength = 0.0;
                float camY        = camPos.y;
                float waterY      = _WaterSurfaceY;

                if (camY < waterY)
                {
                    // Camera is BELOW the water surface.
                    // Ray starts inside water. It exits either at scene geometry,
                    // or (if looking upward) when it crosses the water surface plane.
                    if (rayDir.y > 0.0001)
                    {
                        // Ray going upward — it will eventually hit the surface plane.
                        float tSurface = (waterY - camY) / rayDir.y;
                        waterLength = min(tScene, tSurface);
                    }
                    else
                    {
                        // Ray going downward or horizontal — stays in water until geometry.
                        waterLength = tScene;
                    }
                }
                else
                {
                    // Camera is ABOVE (or at) the water surface.
                    // Ray only travels through water after it crosses the surface plane.
                    if (rayDir.y < -0.0001)
                    {
                        // Ray aimed downward — it enters water at the surface plane.
                        float tEntry = (waterY - camY) / rayDir.y;  // positive: camY > waterY, rayDir.y < 0
                        if (tEntry < tScene)
                        {
                            waterLength = tScene - tEntry;
                        }
                    }
                    // If ray is going up or horizontal and camera is above water,
                    // it never enters the water — waterLength stays 0.
                }

                // Subtract a small near-start offset (avoids fogging pixels right at entry)
                waterLength = max(0.0, waterLength - _UnderwaterFogOffset);

                // Exponential fog: 1 - e^(-density * dist)
                float fogFactor = saturate(1.0 - exp(-_UnderwaterFogDensity * waterLength));

                // _UnderwaterFogWeight smooths the effect at the surface crossing.
                // When camera is above water it's 0 → 1 based on how much of screen is underwater,
                // but we drive it from volume weight for camera-below transitions.
                // For above-water case: fog is self-governing via waterLength, but we still want
                // the smooth entry/exit blend when crossing the surface, so we blend the weight:
                // above water → use fogFactor directly (no weight gate needed; waterLength=0 → fogFactor=0)
                // below water → multiply by _UnderwaterFogWeight for smooth surface transition
                float aboveWater = camY >= waterY ? 1.0 : 0.0;
                float blendWeight = aboveWater + (1.0 - aboveWater) * _UnderwaterFogWeight;
                fogFactor *= blendWeight;

                half3 result = lerp(sceneColor.rgb, _UnderwaterFogColor.rgb, fogFactor);
                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
