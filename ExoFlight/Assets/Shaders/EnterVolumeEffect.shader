Shader "Hidden/ExoFlight/EnterVolumeEffect"
{
    HLSLINCLUDE
        #pragma target 3.0

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float  _Intensity;
        float4 _TintColor;
        float  _WaveStrength;
        float  _WaveFrequency;

        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;

            float wave = sin(uv.y * _WaveFrequency + _Time.y * 2.0) * _WaveStrength * _Intensity;

            // _BlitTexture — кадр до эффекта
            float3 src = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(wave, 0.0), _BlitMipLevel).rgb;

            float3 col = lerp(src, src * _TintColor.rgb, _Intensity);

            float2 d = abs(uv - 0.5) * 2.0;
            col *= 1.0 - smoothstep(0.55, 1.0, max(d.x, d.y)) * _Intensity;

            return float4(col, 1.0);
        }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off ZTest Always Blend Off Cull Off

        Pass
        {
            Name "EnterVolumeEffect"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment Frag
            ENDHLSL
        }
    }

    Fallback Off
}
