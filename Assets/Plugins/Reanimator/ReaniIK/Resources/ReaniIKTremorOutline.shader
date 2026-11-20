Shader "Reanimator/Tremor Outline"
{
 Properties {
 _MainTex("Surface color",2D)="white"{}
 _SurfaceMap("Redness mask",2D)="black"{}
 _Color("Tint",Color)=(1,1,1,1)
 _RedColor("Redness color",Color)=(1,0,0,1)
 _Darkness("Darkness",Float)=0
 _Contrast("Contrast",Range(-1,3))=1
 _Desaturation("Desaturation",Range(0,1))=0
 _ColorDepthPixels("Color depth (pixels)",Float)=3
 _Redness("Redness",Float)=0
 _HasMap("Has mask",Float)=0
 _SurfaceUVX("Surface UV X",Vector)=(1,0,0,0)
 _SurfaceUVY("Surface UV Y",Vector)=(0,1,0,0)
 }
 SubShader {
 Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
 Blend SrcAlpha OneMinusSrcAlpha
 Cull Off ZWrite Off
 Pass {
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
 TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
 CBUFFER_START(UnityPerMaterial)
 half4 _Color, _RedColor;
 float _Darkness, _Redness, _HasMap, _ColorDepthPixels;
 float _Contrast, _Desaturation;
 float4 _MainTex_TexelSize;
 float4 _SurfaceUVX, _SurfaceUVY;
 CBUFFER_END
 struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 bary:TEXCOORD1; float2 innerUV:TEXCOORD2; float3 inward:TEXCOORD3; };
 struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 bary:TEXCOORD1; };
 V vert(A v) {
 V o; float t=0;
 // Mesh contours may include transparent padding. Attach to the first opaque
 // surface texel, moving only this independent VFX card, never the sprite.
 [loop] for(int i=0;i<=20;i++) {
 t=i/20.0;
 if(SAMPLE_TEXTURE2D_LOD(_MainTex,sampler_MainTex,lerp(v.uv,v.innerUV,t),0).a>=0.85) break;
 }
 o.uv=lerp(v.uv,v.innerUV,t);
 // Move the color sample inward in source texels, leaving the VFX anchor intact.
 float2 remaining=v.innerUV-o.uv;
 float pixelDistance=length(remaining*_MainTex_TexelSize.zw);
 o.uv+=remaining*min(1,max(0,_ColorDepthPixels)/max(pixelDistance,0.00001));
 o.positionCS=TransformObjectToHClip(v.positionOS.xyz+v.inward*t);
 o.bary=v.bary;return o;
 }
 half4 frag(V v):SV_Target {
 half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*_Color;
 float3 p=float3(v.uv,1);
 float2 uv=float2(dot(_SurfaceUVX.xyz,p),dot(_SurfaceUVY.xyz,p));
 float red=saturate(SAMPLE_TEXTURE2D(_SurfaceMap,sampler_SurfaceMap,uv).a*_Redness*_HasMap);
 c.rgb*=lerp(half3(1,1,1),saturate(_RedColor.rgb),red);
 // A fine dark rim on the two needle sides, without a line across its base.
 float edge=min(v.bary.x,v.bary.z);
 float rim=1-smoothstep(0,max(fwidth(edge)*0.8,0.0001),edge);
 c.rgb*=lerp(1,0.32,rim);
 c.rgb=max((c.rgb-0.5)*_Contrast+0.5,0);
 half gray=dot(c.rgb,half3(0.2126,0.7152,0.0722));
 c.rgb=lerp(c.rgb,gray.xxx,saturate(_Desaturation));
 c.rgb*=1-saturate(_Darkness);
 return c;
 }
 ENDHLSL
 }
 }
}
