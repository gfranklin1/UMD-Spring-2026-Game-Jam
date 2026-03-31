// Low-poly ocean shader with vertex-colour foam blending.
// Vertex RED channel = foam amount (0 = ocean, 1 = white foam).
// Supports URP forward pass + receives directional-light shadows.

Shader "Custom/OceanWater"
{
    Properties
    {
        _BaseColor      ("Ocean Color",     Color)        = (0.04, 0.20, 0.42, 0.88)
        _FoamColor      ("Foam Color",      Color)        = (0.95, 0.97, 1.00, 1.00)
        _DeepColor      ("Deep Color",      Color)        = (0.01, 0.08, 0.20, 1.00)
        _DepthFade      ("Depth Blend",     Range(0,1))   = 0.4
        _Smoothness     ("Smoothness",      Range(0,1))   = 0.75
        _FoamSharpness  ("Foam Sharpness",  Range(1,8))   = 3.0
        _DepthFadeDistance ("Depth Fade Distance", Float)     = 8.0
        _MinAlpha          ("Min Surface Alpha",   Range(0,1)) = 0.60
        _MaxAlpha          ("Max Surface Alpha",   Range(0,1)) = 0.97
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull   Off         // visible from below the water too

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            // URP keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // R = foam, G = depth-hint (unused yet)
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float4 color       : COLOR;
                float4 screenPos   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _FoamColor;
                float4 _DeepColor;
                float  _DepthFade;
                float  _Smoothness;
                float  _FoamSharpness;
                float  _DepthFadeDistance;
                float  _MinAlpha;
                float  _MaxAlpha;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = posInputs.positionCS;
                OUT.positionWS = posInputs.positionWS;
                OUT.normalWS   = nrmInputs.normalWS;
                OUT.color      = IN.color;
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ── Lighting ────────────────────────────────────────────────
                float3 normalWS   = normalize(IN.normalWS);
                Light  mainLight  = GetMainLight();
                float  NdotL      = saturate(dot(normalWS, mainLight.direction));
                float3 ambient    = SampleSH(normalWS);
                float3 lightColor = mainLight.color * NdotL + ambient;

                // ── Foam from vertex color R channel ────────────────────────
                float rawFoam  = IN.color.r;
                float foam     = pow(saturate(rawFoam), 1.0 / _FoamSharpness);

                // ── Base ocean colour (shallow/deep blend by normal tilt) ───
                float depthBlend = saturate(1.0 - abs(normalWS.y) * (1.0 - _DepthFade));
                float4 oceanCol  = lerp(_BaseColor, _DeepColor, depthBlend);

                // ── Blend ocean → foam ──────────────────────────────────────
                float4 col = lerp(oceanCol, _FoamColor, foam);

                // Apply simple diffuse lighting (foam stays bright)
                float3 litColor = col.rgb * lerp(lightColor, float3(1,1,1), foam * 0.7);
                col.rgb = litColor;

                // Specular highlight (GGX approximation) on non-foam areas
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float  spec    = pow(saturate(dot(normalWS, halfDir)), 64.0 * _Smoothness + 1.0);
                col.rgb += mainLight.color * spec * (1.0 - foam) * _Smoothness;

                // ── Depth-based surface opacity ───────────────────────────────────
                float2 screenUV    = IN.screenPos.xy / IN.screenPos.w;
                float  rawDepth    = SampleSceneDepth(screenUV);
                float  sceneEye    = LinearEyeDepth(rawDepth, _ZBufferParams);
                float  waterEye    = IN.screenPos.w;
                float  waterColumn = sceneEye - waterEye;
                float  depthT      = saturate(waterColumn / _DepthFadeDistance);
                float  baseAlpha   = lerp(_MinAlpha, _MaxAlpha, depthT);
                col.a = lerp(baseAlpha, _FoamColor.a, foam);   // foam stays opaque; ocean scales by depth

                return col;
            }
            ENDHLSL
        }

        // Shadow caster pass so water can receive (not cast) shadows properly
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack "Universal Render Pipeline/Unlit"
}
