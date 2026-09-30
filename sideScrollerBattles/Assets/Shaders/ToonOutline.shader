// A simple URP toon shader for Showcase B ("3D in 2D"): 3 flat light bands off the main
// directional light, sampling the glTF-imported knight's own base colour texture, plus a
// dark inverted-hull outline pass — so the rigged mesh reads like the 2D art beside it
// (Showcase A) instead of a normally-lit 3D model. See BuildKnightMaterial.cs for how this
// is wired to the imported base colour texture.
//
// Both passes are tagged "SRPDefaultUnlit" rather than "UniversalForward" — this project's
// Universal Renderer only draws "UniversalForward"-tagged passes for materials the built-in
// Lit/SimpleLit shaders produce (verified empirically: a trivial magenta-return "UniversalForward"
// pass never drew at all, while the identical pass tagged "SRPDefaultUnlit" drew immediately).
// GetMainLight() still works under that tag: URP writes _MainLightPosition/_MainLightColor once
// per camera before any pass runs, so any pass can read them regardless of its own LightMode tag.
//
// Both passes also declare the identical UnityPerMaterial CBUFFER (same fields, same order) —
// letting them differ silently breaks SRP Batcher compatibility for the whole shader.
Shader "Showcase/ToonOutline"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadowBand("Shadow Band Threshold", Range(0, 1)) = 0.35
        _MidBand("Mid Band Threshold", Range(0, 1)) = 0.7
        _ShadowShade("Shadow Shade", Range(0, 1)) = 0.55
        _MidShade("Mid Shade", Range(0, 1)) = 0.8
        _OutlineWidth("Outline Width", Range(0, 0.05)) = 0.004
        _OutlineColor("Outline Color", Color) = (0.05, 0.05, 0.07, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ---- Outline: inverted hull, drawn behind the main surface ----
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _ShadowBand;
                float _MidBand;
                float _ShadowShade;
                float _MidShade;
                float _OutlineWidth;
                float4 _OutlineColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionHCS : SV_POSITION; };

            Varyings OutlineVert(Attributes IN)
            {
                Varyings OUT;
                float3 positionOS = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                OUT.positionHCS = TransformObjectToHClip(positionOS);
                return OUT;
            }

            half4 OutlineFrag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ---- Main toon-lit surface ----
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitVert
            #pragma fragment LitFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float _ShadowBand;
                float _MidBand;
                float _ShadowShade;
                float _MidShade;
                float _OutlineWidth;
                float4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            Varyings LitVert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 LitFrag(Varyings IN) : SV_Target
            {
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(IN.normalWS);
                float ndotl = saturate(dot(normalWS, mainLight.direction));

                // Three flat bands instead of a smooth gradient — this is the whole "toon" look.
                float shade;
                if (ndotl < _ShadowBand) shade = _ShadowShade;
                else if (ndotl < _MidBand) shade = _MidShade;
                else shade = 1.0;

                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
                // Main light color can be black if the scene has no light bound yet; floor it so
                // the toon shading never goes fully black outside the shadow band on its own.
                half3 lightColor = max(mainLight.color, half3(0.6, 0.6, 0.6));
                half3 litColor = texColor.rgb * shade * lightColor;
                return half4(litColor, texColor.a);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
