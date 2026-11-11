Shader "Annie/EnemyDeathDissolve"
{
    // Cuts alpha only; never moves a pixel. The cut time of every point comes from one order map sampled by world position.
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Map ("Order map (R = when this cell goes, 0..1)", 2D) = "white" {}
        _MapMin ("Map min xy (world)", Vector) = (0,0,0,0)
        _MapInv ("Map 1/size xy (1/world)", Vector) = (1,1,0,0)
        _MapCell ("Map cell (world)", Float) = 0.02
        _Progress ("Cut progress (-1 = off)", Float) = -1
        _Rim ("Rim width (world units, 0 = off)", Float) = 0
        _RimColor ("Rim colour", Color) = (1,0.9,0.62,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_Map); SAMPLER(sampler_Map);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float4 _Color; float4 _MapMin; float4 _MapInv; float _MapCell; float _Progress; float _Rim; float4 _RimColor;
            CBUFFER_END
            struct A { float4 pos : POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; float2 wp : TEXCOORD1; };
            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(w);
                o.uv = i.uv; o.col = i.col * _Color; o.wp = w.xy;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.col;
                float2 muv = (i.wp - _MapMin.xy) * _MapInv.xy;
                float T = SAMPLE_TEXTURE2D_LOD(_Map, sampler_Map, muv, 0).r;
                // order gradient from filtered samples one and a half cells either side: smooth, so the rim and the edge cannot crawl or checker
                float2 hv = 1.5 * _MapCell * _MapInv.xy;
                float tx = SAMPLE_TEXTURE2D_LOD(_Map, sampler_Map, muv + float2(hv.x, 0), 0).r - SAMPLE_TEXTURE2D_LOD(_Map, sampler_Map, muv - float2(hv.x, 0), 0).r;
                float ty = SAMPLE_TEXTURE2D_LOD(_Map, sampler_Map, muv + float2(0, hv.y), 0).r - SAMPLE_TEXTURE2D_LOD(_Map, sampler_Map, muv - float2(0, hv.y), 0).r;
                float gl = max(length(float2(tx, ty)) / (3.0 * _MapCell), 0.02);   // order change per world unit
                float dist = (T - _Progress) / gl;                                // world distance inside the cut (> 0: still there)
                float pxW = max(fwidth(i.wp.x), 1e-6);
                float keep = saturate(dist / pxW + 0.5);                          // one pixel of antialiasing, alpha only
                float r = (_Rim > 0.0) ? saturate(1.0 - dist / _Rim) : 0.0;
                float rim = r * r * (3.0 - 2.0 * r) * step(0.0, dist);
                c.rgb = lerp(c.rgb, _RimColor.rgb, rim * _RimColor.a);
                c.a *= keep;
                c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
}
