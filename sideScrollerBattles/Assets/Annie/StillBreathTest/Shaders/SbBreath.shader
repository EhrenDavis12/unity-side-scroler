Shader "Annie/SbBreath"
{
    // Still breathing: colour is sampled at (uv - push) in the same column, push = _Push * P * M (UV of the 1024 texture, v up).
    // Positive push moves content up. Clamp wrap, bilinear, no mipmaps. _Grid > 1 plays a sprite sheet cell (_Cell, row 0 at the top).
    Properties
    {
        _MainTex ("Still or sheet", 2D) = "white" {}
        _PTex ("P gradient", 2D) = "white" {}
        _MTex ("M map", 2D) = "white" {}
        _Push ("Push (uv)", Float) = 0
        _UseMaps ("Use maps 0/1", Float) = 0
        _Grid ("Sheet grid", Float) = 1
        _Cell ("Sheet cell", Float) = 0
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
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_PTex); SAMPLER(sampler_PTex);
            TEXTURE2D(_MTex); SAMPLER(sampler_MTex);
            CBUFFER_START(UnityPerMaterial)
            float _Push; float _UseMaps; float _Grid; float _Cell;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                float push = 0;
                if (_UseMaps > 0.5)
                {
                    float p = SAMPLE_TEXTURE2D(_PTex, sampler_PTex, uv).r;
                    float m = SAMPLE_TEXTURE2D(_MTex, sampler_MTex, uv).r;
                    push = _Push * p * m;
                }
                uv.y -= push;
                uv = saturate(uv);
                if (_Grid > 1.5)
                {
                    float row = floor(_Cell / _Grid);
                    float col = _Cell - row * _Grid;
                    uv = (float2(col, _Grid - 1.0 - row) + uv) / _Grid;
                }
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
