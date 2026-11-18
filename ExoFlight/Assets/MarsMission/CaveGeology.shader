Shader "ExoFlight/Cave geology" {
Properties { _RockMeters("Rock texture scale in metres",Float)=18  _Sand("Sand",2D)="white"{} _Atlas("Basin",2D)="white"{} _Weights("Terrain layers",2D)="red"{} _Rock("Basalt",2D)="white"{} _Dust("Sediment",2D)="white"{} _Relief("Rock normal",2D)="bump"{} }
SubShader {
Tags{"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
Pass {
Tags{"LightMode"="UniversalForwardOnly"} Cull Off
HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma multi_compile_instancing
#pragma multi_compile_fog
#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
#pragma multi_compile_fragment _ _SHADOWS_SOFT
#define _SPECULARHIGHLIGHTS_OFF 1
#define _ENVIRONMENTREFLECTIONS_OFF 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
TEXTURE2D(_Rock); SAMPLER(sampler_Rock);
TEXTURE2D(_Dust); SAMPLER(sampler_Dust);
TEXTURE2D(_Relief); SAMPLER(sampler_Relief);
TEXTURE2D(_Sand);SAMPLER(sampler_Sand);TEXTURE2D(_Atlas);SAMPLER(sampler_Atlas);TEXTURE2D(_Weights);SAMPLER(sampler_Weights);
CBUFFER_START(UnityPerMaterial) float _RockMeters; CBUFFER_END
struct A {float4 p:POSITION;float3 n:NORMAL;float2 edge:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
struct V {float4 p:SV_POSITION;float3 w:TEXCOORD0; half3 n:TEXCOORD1;half fog:TEXCOORD2;half3 vertexLight:TEXCOORD3;half edge:TEXCOORD4;UNITY_VERTEX_OUTPUT_STEREO};
V vert(A a){V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.edge=a.edge.x;o.w=TransformObjectToWorld(a.p.xyz);o.p=TransformWorldToHClip(o.w);o.n=TransformObjectToWorldNormal(a.n);o.vertexLight=VertexLighting(o.w,o.n);o.fog=ComputeFogFactor(o.p.z);return o;}
half4 frag(V i):SV_Target{
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 half3 n=normalize(i.n);half3 view=GetWorldSpaceNormalizeViewDir(i.w);
 half3 w=pow(abs(n),4);w/=max(.001,w.x+w.y+w.z);
 float3 p=i.w/max(.25,_RockMeters);float2 x=p.zy,y=p.xz,z=p.xy;
 half3 base=SAMPLE_TEXTURE2D(_Rock,sampler_Rock,x).rgb*w.x+SAMPLE_TEXTURE2D(_Rock,sampler_Rock,y).rgb*w.y+SAMPLE_TEXTURE2D(_Rock,sampler_Rock,z).rgb*w.z;
 half dust=smoothstep(.51,.93,n.y)*.87;
 half3 dirt=SAMPLE_TEXTURE2D(_Dust,sampler_Dust,i.w.xz/12).rgb;
 half2 rx=UnpackNormal(SAMPLE_TEXTURE2D(_Relief,sampler_Relief,x)).xy;
 half2 ry=UnpackNormal(SAMPLE_TEXTURE2D(_Relief,sampler_Relief,y)).xy;
 half2 rz=UnpackNormal(SAMPLE_TEXTURE2D(_Relief,sampler_Relief,z)).xy;
 half3 bump=half3(0,rx.y,rx.x)*w.x+half3(ry.x,0,ry.y)*w.y+half3(rz.x,rz.y,0)*w.z;
 n=normalize(n+(bump-n*dot(n,bump))*.45*(1-dust));
 SurfaceData s=(SurfaceData)0;s.albedo=lerp(base,dirt,dust);float2 landUV=i.w.xz/float2(930,1000);half4 splat=SAMPLE_TEXTURE2D(_Weights,sampler_Weights,landUV);
 half3 land=SAMPLE_TEXTURE2D(_Sand,sampler_Sand,i.w.xz/17).rgb*splat.r+dirt*splat.g+SAMPLE_TEXTURE2D(_Rock,sampler_Rock,i.w.xz/27).rgb*splat.b+SAMPLE_TEXTURE2D(_Atlas,sampler_Atlas,landUV).rgb*splat.a;
 s.albedo=lerp(s.albedo,land,smoothstep(.1,.99,i.edge));s.normalTS=half3(0,0,1);s.smoothness=.12;s.occlusion=1;s.alpha=1;
 InputData d=(InputData)0;d.positionWS=i.w;d.normalWS=n;d.viewDirectionWS=view;d.shadowCoord=TransformWorldToShadowCoord(i.w);d.bakedGI=SampleSH(n);d.shadowMask=1;d.vertexLighting=i.vertexLight;d.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);
 half4 c=UniversalFragmentPBR(d,s);c.rgb=MixFog(c.rgb,i.fog);return c;
}
ENDHLSL
}
UsePass "Universal Render Pipeline/Lit/ShadowCaster"
UsePass "Universal Render Pipeline/Lit/DepthOnly"
UsePass "Universal Render Pipeline/Lit/DepthNormals"
}}


