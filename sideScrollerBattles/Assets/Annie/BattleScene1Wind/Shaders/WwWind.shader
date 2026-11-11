Shader "Annie/WwWind"
{
    // Wind on a scrolling lane (BsUnlit scroll UV + a sideways push).
    //   colour = lane(uv - (push, 0)),  push = _Peak/_LaneW * M(lane uv) * P(screen x - _WwSpeed*t)
    // M lives in the lane's texture space (rides with the sprite). P lives in screen space (one gust crosses every depth).
    // x wraps with frac (the lanes repeat); never saturate. _Peak = 0 gives exactly the BsUnlit lookup.
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MTex ("M mask (grey, linear)", 2D) = "black" {}
        _PTex ("P wind pattern (grey, linear)", 2D) = "white" {}
        _Rect ("UV rect (u0 v0 u1 v1)", Vector) = (0,0,1,1)
        _Off ("Scroll offset px", Float) = 0
        _LaneW ("Lane width px", Float) = 3840
        _Scroll ("Scroll mode 0/1", Float) = 1
        _Peak ("Peak push px (0 = wind off)", Float) = 0
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
            TEXTURE2D(_MTex); SAMPLER(sampler_MTex);
            TEXTURE2D(_PTex); SAMPLER(sampler_PTex);
            float _WwT;        // global, seconds
            float _WwSpeed;    // global, px/s (pattern moves left to right)
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float4 _Rect; float _Off; float _LaneW; float _Scroll; float _Peak;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 uv;
                if (_Scroll > 0.5) uv = float2((_Off + i.uv.x * 1920.0) / _LaneW, i.uv.y);
                else uv = lerp(_Rect.xy, _Rect.zw, i.uv);
                // derivatives of the unwrapped uv, taken before frac: frac jumps by 1 across the lane seam, and a sample that used the
                // wrapped uv's own derivatives picks a different filter on the two pixel columns at the seam (a 2 px line)
                float2 gx = ddx(uv), gy = ddy(uv);
                half4 c;
                if (_Peak != 0.0)
                {
                    float m = SAMPLE_TEXTURE2D_GRAD(_MTex, sampler_MTex, uv, gx, gy).r;
                    float pu = (i.uv.x * 1920.0 - _WwSpeed * _WwT) / 1920.0;
                    float p = SAMPLE_TEXTURE2D_GRAD(_PTex, sampler_PTex, float2(frac(pu), 0.5), float2(ddx(pu), 0), float2(ddy(pu), 0)).r;
                    float push = _Peak / _LaneW * m * p;
                    c = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, float2(frac(uv.x - push), uv.y), gx, gy);
                }
                else c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
