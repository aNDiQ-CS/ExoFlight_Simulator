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

        float hash11(float p)
        {
            p = frac(p * 0.1031);
            p *= p + 33.33;
            p *= p + p;
            return frac(p);
        }

        float hash21(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float4 Frag(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord.xy;
            float2 size = _BlitTexture_TexelSize.zw;
            float  t = _Time.y;
            float  k = _Intensity;

            // полосы-разрывы: случайные строки уезжают в сторону
            float band = floor(uv.y * 24.0);
            float seeded = hash11(band * 3.1 + floor(t * 8.0));
            uv.x += (hash11(band + floor(t * 8.0)) - 0.5) * 0.06 * k * step(0.7, seeded);

            // дрожание пикселей
            uv += (float2(hash11(floor(uv.y * 400.0) + t * 60.0),
                          hash11(floor(uv.x * 400.0) + t * 60.0)) - 0.5) * 0.002 * k;

            // расхождение каналов
            float3 col;
            col.r = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv + float2(0.004 * k, 0.0), _BlitMipLevel).r;
            col.g = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, _BlitMipLevel).g;
            col.b = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv - float2(0.004 * k, 0.0), _BlitMipLevel).b;

            // белый шум
            float noise = hash21(uv * size + frac(t) * 137.0);
            col = lerp(col, noise.xxx, 0.25 * k);

            // строчная развёртка
            float scan = 0.85 + 0.15 * sin(uv.y * size.y * 1.5 + t * 20.0);
            col *= lerp(1.0, scan, k);

            return float4(col * lerp(float3(1.0, 1.0, 1.0), _TintColor.rgb * 1.6, k * 0.5), 1.0);
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
