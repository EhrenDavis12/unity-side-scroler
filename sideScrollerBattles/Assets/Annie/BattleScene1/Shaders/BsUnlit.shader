Shader "Annie/BsUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rect ("UV rect (u0 v0 u1 v1)", Vector) = (0,0,1,1)
        _Off ("Scroll offset px", Float) = 0
        _LaneW ("Lane width px", Float) = 3840
        _Scroll ("Scroll mode 0/1", Float) = 0
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
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float4 _Rect; float _Off; float _LaneW; float _Scroll;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 uv;
                if (_Scroll > 0.5) uv = float2((_Off + i.uv.x * 1920.0) / _LaneW, i.uv.y);
                else uv = lerp(_Rect.xy, _Rect.zw, i.uv);
                half4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                return half4(c.rgb * c.a, c.a);
            }
            ENDHLSL
        }
    }
}
