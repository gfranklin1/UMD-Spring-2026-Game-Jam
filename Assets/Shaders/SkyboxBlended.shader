Shader "Custom/SkyboxBlended"
{
    Properties
    {
        _Tint     ("Tint Color", Color) = (.5, .5, .5, .5)
        _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _Tex  ("Skybox A (Day)",  Cube) = "grey" {}
        [NoScaleOffset] _Tex2 ("Skybox B (Night)", Cube) = "grey" {}
        _Blend    ("Blend A→B", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _Tex;
            samplerCUBE _Tex2;
            half4 _Tex_HDR;
            half4 _Tex2_HDR;
            half4 _Tint;
            half  _Exposure;
            float _Rotation;
            half  _Blend;

            float3 RotateY(float3 v, float deg)
            {
                float rad = deg * UNITY_PI / 180.0;
                float s, c;
                sincos(rad, s, c);
                return float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c);
            }

            struct appdata_t { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos      : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            v2f vert(appdata_t v)
            {
                v2f o;
                float3 rotated = RotateY(v.vertex.xyz, _Rotation);
                o.pos      = UnityObjectToClipPos(float4(rotated, 1));
                o.texcoord = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 colA = texCUBE(_Tex,  i.texcoord);
                half4 colB = texCUBE(_Tex2, i.texcoord);
                half4 col  = lerp(colA, colB, _Blend);
                half3 c    = DecodeHDR(col, _Tex_HDR);
                c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb * _Exposure;
                return half4(c, 1);
            }
            ENDCG
        }
    }
}
