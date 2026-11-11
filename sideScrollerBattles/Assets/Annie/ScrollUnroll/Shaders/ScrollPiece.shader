Shader "Annie/ScrollPiece"
{
    // ScrollUnroll piece shader. Raw sRGB bytes in, raw bytes out (texture sRGB off, render texture read/write Linear), blend = straight alpha.
    // _Mode 0: the texture as it is.
    // _Mode 1: the texture, columns left of _ClipX discarded (the panel, clipped at the rod's centre).
    // _Mode 2: contact shadow: black, alpha = texture alpha * _ShadowAmp * exp(-(px - _ShadowR) / _ShadowEFold), same clip.
    // _Mode 3: dim: flat _Tint colour (raw bytes / 255), alpha _Alpha, no texture.
    Properties
    {
        [NoScaleOffset] _MainTex ("Texture", 2D) = "white" {}
        _Mode ("Mode", Float) = 0
        _ClipX ("Clip x (screen px)", Float) = 0
        _ShadowR ("Shadow origin x (screen px)", Float) = 0
        _ShadowAmp ("Shadow peak", Float) = 0
        _ShadowEFold ("Shadow e-fold px", Float) = 10
        _Alpha ("Dim alpha", Float) = 0
        _Tint ("Dim colour", Vector) = (0.29, 0.18, 0.10, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _Mode; float _ClipX; float _ShadowR; float _ShadowAmp; float _ShadowEFold; float _Alpha; float4 _Tint;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float wx : TEXCOORD1; };
            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(w);
                o.uv = i.uv;
                o.wx = w.x * 100.0 + 960.0;   // screen px x of this vertex (1 unit = 100 px, 1920 wide, camera at x 0)
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                if (_Mode > 2.5) return half4(_Tint.rgb, _Alpha);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_Mode > 0.5)
                {
                    if (i.wx < _ClipX) discard;
                }
                if (_Mode > 1.5)
                {
                    float d = max(i.wx - _ShadowR, 0.0);
                    float s = _ShadowAmp * exp(-d / _ShadowEFold);
                    return half4(0, 0, 0, c.a * s);
                }
                return c;
            }
            ENDHLSL
        }
    }
}
