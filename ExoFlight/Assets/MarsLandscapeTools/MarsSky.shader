Shader "Mars Landscape/Sky"
{
    Properties
    {
        _Horizon ("Horizon", Color) = (.77,.50,.37,1)
        _Zenith ("Zenith", Color) = (.24,.18,.21,1)
        _Ground ("Below Horizon", Color) = (.49,.26,.18,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Horizon;
                half4 _Zenith;
                half4 _Ground;
            CBUFFER_END
            float _MarsDustCoverage;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 ray = normalize(input.direction);
                half3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(ray.y * 1.48), .56));
                sky = lerp(sky, _Ground.rgb, smoothstep(0, .39, -ray.y));
                float sun = saturate(dot(ray, normalize(float3(-.54,.33,.78))));
                sky += half3(1,.47,.17) * (.38 * pow(sun, 20) + 1.2 * smoothstep(.9994, .9997, sun));
                half3 dustySky = lerp(_Horizon.rgb, _Ground.rgb, saturate(ray.y * .7 + .24));
                return half4(lerp(sky, dustySky, .94 * saturate(_MarsDustCoverage)), 1);
            }
            ENDHLSL
        }
    }
}
