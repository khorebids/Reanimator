Shader "Reanimator/Arm Mask"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _SurfaceMap ("Surface normal RGB / redness A", 2D) = "black" {}
        _SweatTrail ("Sweat tracks", 2D) = "black" {}
        _SweatDropTexture ("Sweat droplet", 2D) = "black" {}
        _SurfaceRedness ("Redness", Range(0,1)) = 0
        _SurfaceDarkness ("Darkness", Range(0,1)) = 0
        _SurfaceContrast ("Contrast", Range(-1,3)) = 1
        _SurfaceDesaturation ("Desaturation", Range(0,1)) = 0
        _SurfaceSweat ("Sweat", Range(0,1)) = 0
        _SurfaceRedColor ("Redness color", Color) = (0.95,0.12,0.15,1)
        [HideInInspector] _SurfaceUVX ("Surface UV X", Vector) = (1,0,0,0)
        [HideInInspector] _SurfaceUVY ("Surface UV Y", Vector) = (0,1,0,0)
        [HideInInspector] _SurfaceAspect ("Surface aspect", Float) = 1
        [HideInInspector] _SweatDropCount ("Sweat count", Float) = 0
        [HideInInspector] _SweatOpacity0 ("Drop opacity 0", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity1 ("Drop opacity 1", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity2 ("Drop opacity 2", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity3 ("Drop opacity 3", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity4 ("Drop opacity 4", Vector) = (1,1,1,1)
        [HideInInspector] _SurfaceHasMap ("Has surface mask", Float) = 0
        [HideInInspector] _SweatDrop0 ("Sweat drop 0", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop1 ("Sweat drop 1", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop2 ("Sweat drop 2", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop3 ("Sweat drop 3", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop4 ("Sweat drop 4", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop5 ("Sweat drop 5", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop6 ("Sweat drop 6", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop7 ("Sweat drop 7", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop8 ("Sweat drop 8", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop9 ("Sweat drop 9", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop10 ("Sweat drop 10", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop11 ("Sweat drop 11", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop12 ("Sweat drop 12", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop13 ("Sweat drop 13", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop14 ("Sweat drop 14", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop15 ("Sweat drop 15", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop16 ("Sweat drop 16", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop17 ("Sweat drop 17", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop18 ("Sweat drop 18", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop19 ("Sweat drop 19", Vector) = (0,0,0,0)
        [MaterialToggle] _ZWrite("ZWrite", Float) = 0
        [HideInInspector] _ArmUVX ("UV to rest X", Vector) = (1,0,0,0)
        [HideInInspector] _ArmUVY ("UV to rest Y", Vector) = (0,1,0,0)
        [HideInInspector] _ArmA ("Shoulder", Vector) = (0,0,0,0)
        [HideInInspector] _ArmB ("Elbow", Vector) = (0,1,0,0)
        [HideInInspector] _ArmC ("Wrist", Vector) = (0,2,0,0)
        [HideInInspector] _ArmD ("Fingertips", Vector) = (0,3,0,0)
        [HideInInspector] _ArmRange ("Arm distance range", Vector) = (0,3,0,0)
        [HideInInspector] _ArmMaskAmount ("Hidden amount", Float) = 0
        [HideInInspector] _ArmMaskFlip ("Flip direction", Float) = 0
        [HideInInspector] _ArmMaskSoftness ("Edge softness", Float) = 0.01

        // Legacy properties. They're here so that materials using this shader can gracefully fallback to the legacy sprite shader.
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags {"Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
        Cull Off
        ZWrite [_ZWrite]

        Pass
        {
            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            #pragma vertex UnlitVertex
            #pragma fragment UnlitFragment

            struct Attributes
            {
                COMMON_2D_INPUTS
                half4 color : COLOR;
                UNITY_SKINNED_VERTEX_INPUTS
            };

            struct Varyings
            {
                COMMON_2D_OUTPUTS
                half4 color : COLOR;
            };

            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/2DCommon.hlsl"
          
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY SKINNED_SPRITE

            // NOTE: Do not ifdef the properties here as SRP batcher can not handle different layouts.
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _ArmUVX, _ArmUVY, _ArmA, _ArmB, _ArmC, _ArmD, _ArmRange;
                float _ArmMaskAmount, _ArmMaskFlip, _ArmMaskSoftness;
                float4 _SurfaceUVX, _SurfaceUVY, _SurfaceRedColor;
                float _SurfaceRedness, _SurfaceDarkness, _SurfaceSweat, _SurfaceAspect, _SweatDropCount, _SurfaceHasMap;
                float _SurfaceContrast, _SurfaceDesaturation;
                float4 _SweatDrop0;
                float4 _SweatOpacity0, _SweatOpacity1, _SweatOpacity2, _SweatOpacity3, _SweatOpacity4;
                float4 _SweatDrop1;
                float4 _SweatDrop2;
                float4 _SweatDrop3;
                float4 _SweatDrop4;
                float4 _SweatDrop5;
                float4 _SweatDrop6;
                float4 _SweatDrop7;
                float4 _SweatDrop8;
                float4 _SweatDrop9;
                float4 _SweatDrop10;
                float4 _SweatDrop11;
                float4 _SweatDrop12;
                float4 _SweatDrop13;
                float4 _SweatDrop14;
                float4 _SweatDrop15;
                float4 _SweatDrop16;
                float4 _SweatDrop17;
                float4 _SweatDrop18;
                float4 _SweatDrop19;
            CBUFFER_END
            TEXTURE2D(_SurfaceMap); SAMPLER(sampler_SurfaceMap);
            TEXTURE2D(_SweatTrail); SAMPLER(sampler_SweatTrail);
            TEXTURE2D(_SweatDropTexture); SAMPLER(sampler_SweatDropTexture);

            float4 SweatDrop(int index)
            {
                if (index == 0) return _SweatDrop0;
                if (index == 1) return _SweatDrop1;
                if (index == 2) return _SweatDrop2;
                if (index == 3) return _SweatDrop3;
                if (index == 4) return _SweatDrop4;
                if (index == 5) return _SweatDrop5;
                if (index == 6) return _SweatDrop6;
                if (index == 7) return _SweatDrop7;
                if (index == 8) return _SweatDrop8;
                if (index == 9) return _SweatDrop9;
                if (index == 10) return _SweatDrop10;
                if (index == 11) return _SweatDrop11;
                if (index == 12) return _SweatDrop12;
                if (index == 13) return _SweatDrop13;
                if (index == 14) return _SweatDrop14;
                if (index == 15) return _SweatDrop15;
                if (index == 16) return _SweatDrop16;
                if (index == 17) return _SweatDrop17;
                if (index == 18) return _SweatDrop18;
                if (index == 19) return _SweatDrop19;
                return 0;
            }

            Varyings UnlitVertex(Attributes input)
            {
                UNITY_SKINNED_VERTEX_COMPUTE(input);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);

                Varyings o = CommonUnlitVertex(input);
                o.color = input.color *_Color * unity_SpriteColor;
                return o;
            }

            // Select the closest segment in the rest pose, then measure cumulative arc length.
            // UVs stay attached to the skin, including under GPU deformation and sprite flips.
            void ArmSegment(float2 p, float2 a, float2 b, float first, float last,
                inout float best, inout float along, inout float offset)
            {
                float2 delta = b - a;
                float segmentLength = max(length(delta), 0.00001);
                float t = dot(p - a, delta) / (segmentLength * segmentLength);
                float2 difference = p - (a + delta * saturate(t));
                float distance = dot(difference, difference);
                if (distance < best)
                {
                    best = distance;
                    along = offset + segmentLength * (((first > 0 && t < 0) || (last > 0 && t > 1)) ? t : saturate(t));
                }
                offset += segmentLength;
            }

            half4 UnlitFragment(Varyings input) : SV_Target
            {
                half4 color = CommonUnlitFragment(input, input.color);
                float3 uv = float3(input.uv, 1);
                float2 p = float2(dot(_ArmUVX.xyz, uv), dot(_ArmUVY.xyz, uv));
                float best = 1e20, along = 0, offset = 0;
                ArmSegment(p, _ArmA.xy, _ArmB.xy, 1, 0, best, along, offset);
                ArmSegment(p, _ArmB.xy, _ArmC.xy, 0, 0, best, along, offset);
                ArmSegment(p, _ArmC.xy, _ArmD.xy, 0, 1, best, along, offset);
                float progress = saturate((along - _ArmRange.x) / max(_ArmRange.y, 0.00001));
                progress = _ArmMaskFlip > 0.5 ? progress : 1 - progress;
                float width = max(0.00001, max(_ArmMaskSoftness, fwidth(progress)));
                float visible = smoothstep(_ArmMaskAmount - width * 0.5, _ArmMaskAmount + width * 0.5, progress);
                if (_ArmMaskAmount <= 0) visible = 1;
                if (_ArmMaskAmount >= 1) visible = 0;
                color.a *= visible;
                float2 surfaceUV = float2(dot(_SurfaceUVX.xyz, uv), dot(_SurfaceUVY.xyz, uv));
                half4 surface = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, surfaceUV);
                float red = saturate(surface.a * _SurfaceRedness * _SurfaceHasMap);
                color.rgb *= lerp(half3(1,1,1), saturate(_SurfaceRedColor.rgb), red);
                float wet = SAMPLE_TEXTURE2D(_SweatTrail, sampler_SweatTrail, surfaceUV).r;
                color.rgb = color.rgb * (1 - wet * 0.07 * _SurfaceSweat) + wet * 0.16 * _SurfaceSweat;
                [loop] for (int i = 0; i < min((int)_SweatDropCount, 20); i++)
                {
                    float4 drop = SweatDrop(i);
                    float2 delta = (surfaceUV - drop.xy) * float2(_SurfaceAspect, 1);
                    float radius = max(drop.z, 0.00001);
                    float sn, cs;
                    sincos(drop.w, sn, cs);
                    // Source points right: rotate its round leading end along the flow.
                    float2 dropUV = float2(dot(delta, float2(cs, sn)), dot(delta, float2(-sn, cs))) / (radius * 6) + 0.5;
                    if (all(dropUV >= 0) && all(dropUV <= 1))
                    {
                        half4 droplet = SAMPLE_TEXTURE2D(_SweatDropTexture, sampler_SweatDropTexture, dropUV);
                        float4 opacity = i < 4 ? _SweatOpacity0 : i < 8 ? _SweatOpacity1 : i < 12 ? _SweatOpacity2 : i < 16 ? _SweatOpacity3 : _SweatOpacity4;
                        color.rgb = lerp(color.rgb, droplet.rgb, droplet.a * _SurfaceSweat * opacity[i & 3]);
                    }
                }
                color.rgb = max((color.rgb - 0.5) * _SurfaceContrast + 0.5, 0);
                half gray = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                color.rgb = lerp(color.rgb, gray.xxx, saturate(_SurfaceDesaturation));
                color.rgb *= 1 - saturate(_SurfaceDarkness);
                return color;
            }
            ENDHLSL
        }
    }
}


