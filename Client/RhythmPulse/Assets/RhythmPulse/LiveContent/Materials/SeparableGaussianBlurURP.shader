Shader "Custom/SeparableGaussianBlurURP"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BlurRadius ("Blur Radius (Sigma)", Float) = 1.0 // Gaussian sigma
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_MainTex);
    SAMPLER(sampler_MainTex);
    float4 _MainTex_TexelSize; // xy: 1/width, 1/height
    half _BlurRadius; // Gaussian sigma, controls spread of weights

    // Gaussian function
    half GetGaussianWeight(half offset, half sigma)
    {
        // sigma_squared = sigma * sigma
        // Instead of full exp, for fixed taps, weights can be precomputed or normalized later.
        // This is a common Gaussian formula:
        return exp(-0.5h * (offset * offset) / (sigma * sigma));
    }

    struct Attributes
    {
        float4 positionOS   : POSITION;
        float2 uv           : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS   : SV_POSITION;
        float2 uv           : TEXCOORD0;
    };

    Varyings Vert(Attributes IN)
    {
        Varyings OUT;
        OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
        OUT.uv = IN.uv;
        return OUT;
    }

    // Common fragment shader logic for 1D Gaussian blur
    // Direction: (1,0) for horizontal, (0,1) for vertical
    half4 FragBlur(Varyings IN, float2 direction)
    {
        half4 accumulatedColor = half4(0.0h, 0.0h, 0.0h, 0.0h);
        half totalWeight = 0.0h;

        // Kernel size: e.g., 15 taps (-7 to +7)
        // For a sigma (BlurRadius), typically 3*sigma covers most of the kernel.
        // If BlurRadius is 1, kernel goes -3 to +3 (7 taps).
        // If BlurRadius is 10, kernel goes -30 to +30 (61 taps).
        // For performance, we use a fixed number of taps, and BlurRadius (sigma) controls weight falloff.
        // Let's use a fixed 15 taps: -7 to +7 offset in texels.
        // _BlurRadius then acts as sigma for these taps.
        // If _BlurRadius is small, outer taps get very low weight.
        // If _BlurRadius is large, weights are more spread out.

        const int KERNEL_TAP_RADIUS = 7; // Results in 2*7+1 = 15 taps

        for (int i = -KERNEL_TAP_RADIUS; i <= KERNEL_TAP_RADIUS; ++i)
        {
            float texelOffset = (float)i;
            half weight = GetGaussianWeight(texelOffset, _BlurRadius);
            
            float2 offsetUV = IN.uv + direction * texelOffset * _MainTex_TexelSize.xy;
            accumulatedColor += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, offsetUV) * weight;
            totalWeight += weight;
        }

        if (totalWeight == 0.0h) totalWeight = 1.0h; // Avoid division by zero
        return accumulatedColor / totalWeight;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        LOD 100
        ZWrite Off Cull Off ZTest Always // Important for Blit

        // Pass 0: Horizontal Blur
        Pass
        {
            Name "BLUR_HORIZONTAL"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHorizontal
            
            half4 FragHorizontal(Varyings IN) : SV_Target
            {
                return FragBlur(IN, float2(1.0h, 0.0h));
            }
            ENDHLSL
        }

        // Pass 1: Vertical Blur
        Pass
        {
            Name "BLUR_VERTICAL"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVertical

            half4 FragVertical(Varyings IN) : SV_Target
            {
                return FragBlur(IN, float2(0.0h, 1.0h));
            }
            ENDHLSL
        }
    }
    Fallback Off
}