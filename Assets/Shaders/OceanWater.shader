// Enhanced low-poly ocean shader with dynamic highlights and reflections.
// No textures — all surface detail comes from procedural sine-wave normals.
//
// Features:
//   • Procedural ripple normals (6 summed sine waves, analytical derivatives)
//   • Fresnel effect — grazing angles reflect skybox; head-on shows water colour
//   • Environment reflections via URP reflection probes / screen-space (URP 17+)
//   • GGX specular highlights from directional light
//   • Subtle color variation — wave crests brighten, troughs darken
//   • Foam from vertex color R channel; depth-based surface opacity
//
// URP 17 forward pass. Vertex RED = foam (0=ocean, 1=white foam).

Shader "Custom/OceanWater"
{
    Properties
    {
        _BaseColor          ("Ocean Color",             Color)        = (0.04, 0.20, 0.42, 0.92)
        _FoamColor          ("Foam Color",              Color)        = (0.95, 0.97, 1.00, 1.00)
        _DeepColor          ("Deep Color",              Color)        = (0.01, 0.08, 0.20, 1.00)
        _DepthFade          ("Depth Blend",             Range(0,1))   = 0.4
        _Smoothness         ("Smoothness",              Range(0,1))   = 0.88
        _FoamSharpness      ("Foam Sharpness",          Range(1,8))   = 3.0
        _DepthFadeDistance  ("Depth Fade Distance",     Float)        = 8.0
        _MinAlpha           ("Min Surface Alpha",       Range(0,1))   = 0.60
        _MaxAlpha           ("Max Surface Alpha",       Range(0,1))   = 0.97
        // ── Fresnel & Reflections ──────────────────────────────────────────
        _FresnelPower       ("Fresnel Power",           Range(1,8))   = 4.0
        _ReflectionStrength ("Reflection Strength",     Range(0,2))   = 1.0
        // ── Detail Normals (procedural ripples) ───────────────────────────
        _NormalStrength     ("Detail Normal Strength",  Range(0,3))   = 0.7
        _NormalScale        ("Detail Normal Scale",     Float)        = 0.5
        _NormalSpeed        ("Detail Normal Speed",     Float)        = 0.4
        // ── Color Variation ───────────────────────────────────────────────
        _ColorVariation     ("Color Variation",         Range(0,0.5)) = 0.12
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
            Cull   Off          // visible from below too

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ── Structs ──────────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;      // R = foam
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float4 color      : COLOR;
                float4 screenPos  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Material constants ───────────────────────────────────────────

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
                float  _FresnelPower;
                float  _ReflectionStrength;
                float  _NormalStrength;
                float  _NormalScale;
                float  _NormalSpeed;
                float  _ColorVariation;
            CBUFFER_END

            // ── Procedural detail normal ─────────────────────────────────────
            //
            // Sums 6 sine waves h_i = A_i * sin(k_i · p + ω_i * t)
            // and returns the surface normal from their analytical partial derivatives:
            //   dH/dx = Σ A_i * kx_i * cos(phase_i)
            //   dH/dz = Σ A_i * kz_i * cos(phase_i)
            //   N = normalize(-dH/dx, 1, -dH/dz)
            //
            // No tiling artefacts because waves have irrational frequency ratios.

            float3 DetailNormal(float3 worldPos)
            {
                float s = _NormalScale;
                float t = _Time.y * _NormalSpeed;
                float dhdx = 0.0, dhdz = 0.0;
                float ph, c;

                // Layer 1 — primary swell direction
                ph = s*0.80*worldPos.x + s*0.60*worldPos.z + 1.20*t;
                c  = cos(ph);
                dhdx += c * s*0.80 * 0.22;  dhdz += c * s*0.60 * 0.22;

                // Layer 2 — cross-swell
                ph = s*1.30*worldPos.x - s*0.90*worldPos.z + 0.85*t;
                c  = cos(ph);
                dhdx += c * s*1.30 * 0.16;  dhdz -= c * s*0.90 * 0.16;

                // Layer 3 — diagonal chop
                ph = s*0.45*worldPos.x + s*1.55*worldPos.z + 1.50*t;
                c  = cos(ph);
                dhdx += c * s*0.45 * 0.13;  dhdz += c * s*1.55 * 0.13;

                // Layer 4 — counter-diagonal
                ph = s*1.80*worldPos.x - s*0.35*worldPos.z + 0.70*t;
                c  = cos(ph);
                dhdx += c * s*1.80 * 0.09;  dhdz -= c * s*0.35 * 0.09;

                // Layer 5 — fine sparkle (higher frequency)
                ph = s*2.50*worldPos.x + s*0.70*worldPos.z + 1.90*t;
                c  = cos(ph);
                dhdx += c * s*2.50 * 0.06;  dhdz += c * s*0.70 * 0.06;

                // Layer 6 — slow long-period ripple
                ph = s*0.30*worldPos.x + s*2.20*worldPos.z + 1.10*t;
                c  = cos(ph);
                dhdx += c * s*0.30 * 0.08;  dhdz += c * s*2.20 * 0.08;

                return normalize(float3(-dhdx, 1.0, -dhdz));
            }

            // ── Vertex shader ────────────────────────────────────────────────

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

            // ── Fragment shader ──────────────────────────────────────────────

            half4 frag(Varyings IN) : SV_Target
            {
                float3 posWS   = IN.positionWS;
                float3 viewDir = normalize(_WorldSpaceCameraPos - posWS);

                // ── Normals ──────────────────────────────────────────────────
                float3 meshNormal   = normalize(IN.normalWS);
                float3 detailNormal = DetailNormal(posWS);

                // Combine large-scale mesh tilt with fine ripple detail.
                // Both contribute XZ perturbation; Y=1 bias keeps normal upward.
                float2 combinedXZ  = meshNormal.xz + detailNormal.xz * _NormalStrength;
                float3 blendNormal = normalize(float3(combinedXZ.x, 1.0, combinedXZ.y));

                // ── Foam ─────────────────────────────────────────────────────
                float foam = pow(saturate(IN.color.r), 1.0 / _FoamSharpness);

                // ── Base colour ──────────────────────────────────────────────
                // Steep wave faces (low normalWS.y) → blend toward deep colour
                float depthBlend = saturate(1.0 - abs(meshNormal.y) * (1.0 - _DepthFade));
                float3 oceanRGB  = lerp(_BaseColor.rgb, _DeepColor.rgb, depthBlend);

                // Wave crests (detail normal points straight up) → slightly brighter
                float upFacing = saturate(dot(detailNormal, float3(0, 1, 0)));
                oceanRGB *= lerp(1.0 - _ColorVariation, 1.0 + _ColorVariation, upFacing);

                // ── Lighting ─────────────────────────────────────────────────
                Light  mainLight = GetMainLight();
                float  NdotL     = saturate(dot(blendNormal, mainLight.direction));
                float3 ambient   = SampleSH(blendNormal);
                float3 lightCol  = mainLight.color * NdotL + ambient;

                // Water diffuse + lit foam
                float3 diffuse = lerp(oceanRGB * lightCol,
                                      _FoamColor.rgb * lightCol,
                                      foam);

                // ── GGX Specular ─────────────────────────────────────────────
                // Tight, physically correct highlight from the sun.
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float  NdotH   = saturate(dot(blendNormal, halfDir));
                float  NdotV   = max(dot(blendNormal, viewDir), 0.001);
                float  r       = max(1.0 - _Smoothness, 0.001);
                float  r4      = r * r * r * r;
                float  d       = NdotH * NdotH * (r4 - 1.0) + 1.0;
                float  specD   = r4 / max(UNITY_PI * d * d, 1e-5);
                float3 specCol = mainLight.color * specD * NdotL * _Smoothness * (1.0 - foam);

                // ── Fresnel ──────────────────────────────────────────────────
                // Physical water R0 ≈ 0.02 (refractive index 1.33 / Schlick).
                // At grazing angles (NdotV → 0) reflectance → 1.0.
                float fresnel     = pow(1.0 - saturate(NdotV), _FresnelPower);
                float reflectance = lerp(0.02, 1.0, fresnel);

                // ── Environment reflections ───────────────────────────────────
                // Samples the nearest reflection probe (or SSR if enabled in renderer).
                // blendNormal perturbs the reflection direction → wavy mirror effect.
                float3 reflectDir   = reflect(-viewDir, blendNormal);
                float  perceptRough = 1.0 - _Smoothness;
                float2 screenUV     = IN.screenPos.xy / IN.screenPos.w;
                half3  envRefl      = GlossyEnvironmentReflection(
                    (half3)reflectDir, posWS,
                    (half)perceptRough, (half)1.0,
                    screenUV);

                // Fresnel blend: head-on → water colour; grazing → reflection
                float  reflBlend = saturate(reflectance * _ReflectionStrength);
                float3 result    = lerp(diffuse, (float3)envRefl, reflBlend * (1.0 - foam));

                // Additive specular on top of everything
                result += specCol;

                // ── Depth-based surface opacity ───────────────────────────────
                float rawDepth  = SampleSceneDepth(screenUV);
                float sceneEye  = LinearEyeDepth(rawDepth, _ZBufferParams);
                float waterEye  = IN.screenPos.w;
                float depthT    = saturate((sceneEye - waterEye) / _DepthFadeDistance);
                float baseAlpha = lerp(_MinAlpha, _MaxAlpha, depthT);
                float alpha     = lerp(baseAlpha, _FoamColor.a, foam);

                return half4(result, alpha);
            }
            ENDHLSL
        }

        // Water receives (not casts) shadows via the opaque lit shadow caster
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }

    FallBack "Universal Render Pipeline/Unlit"
}
