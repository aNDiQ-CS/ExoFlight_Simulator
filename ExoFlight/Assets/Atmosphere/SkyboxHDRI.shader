Shader "ExoFlight/Skybox HDRI"
{
    // Placeholder skybox: drop an equirectangular HDRI into _MainTex and you are done.
    // Without a texture (_UseGradient = 1) it draws a Mars gradient with a blue sun glow.
    Properties
    {
        [NoScaleOffset] _MainTex ("HDRI (equirect 2:1)", 2D) = "grey" {}
        _UseGradient ("No HDRI: use gradient", Range(0,1)) = 1
        _Rotation ("Rotation (deg)", Range(0,360)) = 0
        _Exposure ("Exposure", Range(0,8)) = 1
        [HDR] _Tint ("Tint", Color) = (1,1,1,1)

        [Header(Mars gradient)]
        [HDR] _HorizonColor ("Horizon", Color) = (1.0,0.55,0.28,1)
        [HDR] _ZenithColor ("Zenith", Color) = (0.32,0.26,0.22,1)
        _GradientPower ("Gradient power", Range(0.1,2)) = 0.45
        _HorizonHeight ("Horizon height", Range(-1,1)) = 0.0

        [Header(Sun)]
        _SunDir ("Sun direction (world)", Vector) = (0.3,0.12,1,0)
        [HDR] _SunColor ("Sun colour (blue at sunset)", Color) = (0.45,0.7,1,1)
        _SunSize ("Sun disc size (rad²: 0.0003 ≈ 1.4°)", Range(0.00001,0.01)) = 0.00035
        _SunDisc ("Sun disc intensity", Range(0,40)) = 8
        _SunGlow ("Sun glow intensity", Range(0,10)) = 2.5
        _SunGlowPower ("Sun glow tightness", Range(4,200)) = 30
        _HaloGlow ("Blue aerosol halo (sunset)", Range(0,4)) = 0.9

        [Header(Storm)]
        _Storm ("Storm 0..1", Range(0,1)) = 0
        [HDR] _DustColor ("Dust colour", Color) = (0.62,0.42,0.26,1)
        _DustContrast ("Storm contrast", Range(0,1)) = 0.7
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _UseGradient;
            float _Rotation;
            float _Exposure;
            float4 _Tint;

            float4 _HorizonColor;
            float4 _ZenithColor;
            float _GradientPower;
            float _HorizonHeight;

            float4 _SunDir;
            float4 _SunColor;
            float _SunSize;
            float _SunDisc;
            float _SunGlow;
            float _SunGlowPower;
            float _HaloGlow;

            float _Storm;
            float4 _DustColor;
            float _DustContrast;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            float3 RotateY(float3 d, float degrees)
            {
                float r = radians(degrees);
                float s = sin(r);
                float c = cos(r);
                return float3(d.x * c - d.z * s, d.y, d.x * s + d.z * c);
            }

            // equirectangular (latlong) mapping used for HDRIs
            float2 DirToEquirect(float3 d)
            {
                float u = atan2(d.z, d.x) * (1.0 / UNITY_TWO_PI) + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) * (1.0 / UNITY_PI) + 0.5;
                return float2(u, v);
            }

            float3 MarsGradient(float3 d)
            {
                float h = saturate((d.y - _HorizonHeight) * 1.5 + 0.5);
                float3 col = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(h, _GradientPower));

                float3 sunDir = normalize(_SunDir.xyz);
                float s = saturate(dot(d, sunDir));
                float disc = smoothstep(1.0 - _SunSize, 1.0, s);
                float glow = pow(s, _SunGlowPower);
                col += _SunColor.rgb * (disc * _SunDisc + glow * _SunGlow);

                // Mars: fine dust scatters light forward, so the sky right around the sun
                // turns blue even though the rest of the sky is butterscotch
                col += _SunColor.rgb * pow(s, 12.0) * _HaloGlow;

                // haze near the horizon sells the distance and hides the level edge
                col = lerp(col, _HorizonColor.rgb, pow(1.0 - h, 3.0) * 0.35);
                return col;
            }

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = normalize(v.vertex.xyz);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 dir = RotateY(normalize(i.dir), _Rotation);

                float3 col;
                float3 tex = tex2D(_MainTex, DirToEquirect(dir)).rgb * _Exposure;
                float3 grad = MarsGradient(dir) * _Exposure;
                col = lerp(tex, grad, saturate(_UseGradient));

                // storm: flatten the sky toward a dust wall, keeping a little contrast
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                float3 dust = _DustColor.rgb * lerp(1.0, lum, _DustContrast) * _Exposure;
                col = lerp(col, dust, saturate(_Storm));

                return float4(col * _Tint.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
