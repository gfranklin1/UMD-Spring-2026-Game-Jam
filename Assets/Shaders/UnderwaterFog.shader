// Fullscreen underwater distance fog using the depth buffer.
// Objects fade toward _UnderwaterFogColor based on camera distance (exponential falloff).
// Enabled per-frame via Shader.SetGlobalFloat("_UnderwaterFogEnabled", 0|1).
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
            float  _UnderwaterFogWeight;    // 0-1 master blend (mirrors volume weight for smooth entry/exit)

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;

                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // Early-out — no cost when fully above water
                if (_UnderwaterFogWeight < 0.001)
                    return sceneColor;

                float rawDepth    = SampleSceneDepth(uv);
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                // Distance past the near-start offset
                float fogDist = max(0.0, linearDepth - _UnderwaterFogOffset);

                // Exponential fog:  1 - e^(-density * dist)
                // At density=0.08 and 20m: factor ≈ 0.80  (scene almost gone at 20m)
                float fogFactor = 1.0 - exp(-_UnderwaterFogDensity * fogDist);
                fogFactor = saturate(fogFactor) * _UnderwaterFogWeight;

                half3 result = lerp(sceneColor.rgb, _UnderwaterFogColor.rgb, fogFactor);
                return half4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
}
