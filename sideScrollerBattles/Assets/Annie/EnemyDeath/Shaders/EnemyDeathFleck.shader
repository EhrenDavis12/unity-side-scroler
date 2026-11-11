Shader "Annie/EnemyDeathFleck"
{
    // Particle sprite: the drawn PNG gives the shape and the alpha; the grey outline baked into the white shard texture is
    // lifted toward the tint so a pale tint stays pale at 3 to 7 px (_Lift 0 = texture as drawn, 1 = flat tint).
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Lift ("Outline lift", Range(0,1)) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Cull Off ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; float _Lift;
            CBUFFER_END
            struct A { float4 pos : POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 lit = lerp(t.rgb, half3(1,1,1), _Lift);
                half a = t.a * i.col.a;
                return half4(lit * i.col.rgb * a, a);
            }
            ENDHLSL
        }
    }
}
