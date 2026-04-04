// Seabed shader — reads per-vertex colour as albedo so the C# mesh generator
// can paint the floor based on depth (shallow = warm sand, deep = dark rock/mud).
// No textures required. Basic diffuse + ambient; no shadow casting or receiving
// (the seabed is always lit by ambient/directional light only).

Shader "Custom/SeabedVertexColor"
{
    Properties
    {
        _Smoothness ("Smoothness",  Range(0, 1))   = 0.08
        _Metallic   ("Metallic",    Range(0, 1))   = 0.0
        _Brightness ("Brightness",  Range(0.5, 2)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Opaque"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Geometry"
        }
        LOD 200

        // ── Forward Lit ────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // Fog
            #pragma multi_compile_fog

            // Additional lights (point/spot)
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 vertColor  : COLOR;       // depth-baked colour from C#
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 vertColor  : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float _Smoothness;
                float _Metallic;
                float _Brightness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.vertColor  = IN.vertColor.rgb * _Brightness;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 albedo  = IN.vertColor;
                float3 normalW = normalize(IN.normalWS);

                // ── Main directional light (diffuse only, no shadows on seabed) ──
                Light mainLight = GetMainLight();
                float NdotL   = saturate(dot(normalW, mainLight.direction));
                float3 diffuse = albedo * mainLight.color * NdotL * 0.8;

                // ── Spherical harmonics ambient ───────────────────────────────────
                float3 ambient = SampleSH(normalW) * albedo;

                float3 color = ambient + diffuse;

                // ── Underwater fog ────────────────────────────────────────────────
                color = MixFog(color, IN.fogFactor);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Depth-only pass so transparent objects (water surface) can depth-test correctly
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
