Shader "Yaui/Samples/Gradient Tint"
{
    // A custom shader for YauiElement.Material: the uber shader's result (FragImpl) tinted with a horizontal
    // gradient. Custom shaders include Yaui.hlsl and declare the "Overlay" and "World" passes like Uber.shader.
    Properties
    {
        _ColorA ("Left", Color) = (1, 0.3, 0.6, 1)
        _ColorB ("Right", Color) = (0.3, 0.6, 1, 1)

        // Set by the renderer inside masks (YauiMask).
        [HideInInspector] _YauiStencilRef ("Stencil Ref", Float) = 0
        [HideInInspector] _YauiStencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _YauiStencilPass ("Stencil Pass", Float) = 0
    }

    HLSLINCLUDE
    #pragma target 4.5
    // Rotated quads and rounded clips are clipped per pixel only with this (it costs GPU time).
    #define YAUI_PIXEL_CLIP
    #include "Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl"

    float4 _ColorA;
    float4 _ColorB;

    half4 Tint(Varyings i, half4 color)
    {
        // local: position relative to the shape center, and its half size.
        float t = saturate(i.local.x / max(i.local.z, 1e-4) * 0.5 + 0.5);
        return color * half4(lerp(_ColorA.rgb, _ColorB.rgb, t), 1.0);
    }

    half4 FragTintOverlay(Varyings i) : SV_Target { return Tint(i, FragImpl(i, false)); }
    half4 FragTintWorld(Varyings i) : SV_Target { return Tint(i, FragImpl(i, true)); }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Stencil
        {
            Ref [_YauiStencilRef]
            Comp [_YauiStencilComp]
            Pass [_YauiStencilPass]
        }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "YauiOverlay" }
            ZTest Always

            HLSLPROGRAM
            #pragma vertex VertOverlay
            #pragma fragment FragTintOverlay
            ENDHLSL
        }

        Pass
        {
            Name "World"
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex VertWorld
            #pragma fragment FragTintWorld
            ENDHLSL
        }
    }
}
