Shader "Hidden/Yaui/AtlasBlit"
{
    // Draws a sprite into a dynamic atlas page: covers the allocation, and clamps to the sprite's rect so that the
    // padding repeats its edges (no bleeding with bilinear filtering).
    SubShader
    {
        ZTest Always
        ZWrite Off
        Cull Off
        Blend Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            TEXTURE2D(_YauiAtlasSource);
            SAMPLER(sampler_YauiAtlasSource);
            float4 _YauiAtlasSource_TexelSize;

            // Normalized: xy min, zw max.
            float4 _YauiAtlasSourceRect;
            float4 _YauiAtlasDestinationRect;
            float4 _YauiAtlasInnerRect;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 pageUv : TEXCOORD0;
            };

            Varyings Vert(uint vertexId : SV_VertexID)
            {
                static const uint corners[6] = {0, 1, 2, 2, 1, 3};
                uint corner = corners[vertexId];
                float2 t = float2(corner & 1u, corner >> 1);
                float2 pageUv = lerp(_YauiAtlasDestinationRect.xy, _YauiAtlasDestinationRect.zw, t);
                float2 clip = pageUv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                clip.y = -clip.y;
                #endif
                Varyings o;
                o.positionCS = float4(clip, 0.0, 1.0);
                o.pageUv = pageUv;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float2 t = saturate(
                    (i.pageUv - _YauiAtlasInnerRect.xy) / (_YauiAtlasInnerRect.zw - _YauiAtlasInnerRect.xy));
                float2 halfTexel = _YauiAtlasSource_TexelSize.xy * 0.5;
                float2 uv = clamp(lerp(_YauiAtlasSourceRect.xy, _YauiAtlasSourceRect.zw, t),
                                                             _YauiAtlasSourceRect.xy + halfTexel,
                                                             _YauiAtlasSourceRect.zw - halfTexel);
                return SAMPLE_TEXTURE2D_LOD(_YauiAtlasSource, sampler_YauiAtlasSource, uv, 0);
            }
            ENDHLSL
        }
    }
}