Shader "Annie/GbMix"
{
    // Ground blend: lerp(grass, soil, mask). White mask = soil, black mask = grass.
    // Same scroll UV as BsUnlit lanes (_Off, _LaneW, _Scroll written by BsDirector); all three textures share it.
    Properties
    {
        _SoilTex ("Soil", 2D) = "white" {}
        _GrassTex ("Grass", 2D) = "gray" {}
        _MaskTex ("Mask (white = soil)", 2D) = "white" {}
        _MaskMul ("Mask multiplier", Float) = 1
        _MaskAdd ("Mask add", Float) = 0
        _Off ("Scroll offset px", Float) = 0
        _LaneW ("Lane width px", Float) = 3840
        _Scroll ("Scroll mode 0/1", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_SoilTex); SAMPLER(sampler_SoilTex);
            TEXTURE2D(_GrassTex); SAMPLER(sampler_GrassTex);
            TEXTURE2D(_MaskTex); SAMPLER(sampler_MaskTex);
            CBUFFER_START(UnityPerMaterial)
            float _MaskMul; float _MaskAdd; float _Off; float _LaneW; float _Scroll;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 uv = (_Scroll > 0.5) ? float2((_Off + i.uv.x * 1920.0) / _LaneW, i.uv.y) : i.uv;
                half4 soil = SAMPLE_TEXTURE2D(_SoilTex, sampler_SoilTex, uv);
                half3 grass = SAMPLE_TEXTURE2D(_GrassTex, sampler_GrassTex, uv).rgb;
                half m = saturate(SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, uv).r * _MaskMul + _MaskAdd);
                half3 c = lerp(grass, soil.rgb, m);
                return half4(c * soil.a, soil.a);
            }
            ENDHLSL
        }
    }
}
