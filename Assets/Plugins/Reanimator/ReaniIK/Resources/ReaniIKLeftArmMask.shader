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
        [HideInInspector] _SweatOpacity5 ("Drop opacity 5", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity6 ("Drop opacity 6", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity7 ("Drop opacity 7", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity8 ("Drop opacity 8", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity9 ("Drop opacity 9", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity10 ("Drop opacity 10", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity11 ("Drop opacity 11", Vector) = (1,1,1,1)
        [HideInInspector] _SweatOpacity12 ("Drop opacity 12", Vector) = (1,1,1,1)
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
        [HideInInspector] _SweatDrop20 ("Sweat drop 20", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop21 ("Sweat drop 21", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop22 ("Sweat drop 22", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop23 ("Sweat drop 23", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop24 ("Sweat drop 24", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop25 ("Sweat drop 25", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop26 ("Sweat drop 26", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop27 ("Sweat drop 27", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop28 ("Sweat drop 28", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop29 ("Sweat drop 29", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop30 ("Sweat drop 30", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop31 ("Sweat drop 31", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop32 ("Sweat drop 32", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop33 ("Sweat drop 33", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop34 ("Sweat drop 34", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop35 ("Sweat drop 35", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop36 ("Sweat drop 36", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop37 ("Sweat drop 37", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop38 ("Sweat drop 38", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop39 ("Sweat drop 39", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop40 ("Sweat drop 40", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop41 ("Sweat drop 41", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop42 ("Sweat drop 42", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop43 ("Sweat drop 43", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop44 ("Sweat drop 44", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop45 ("Sweat drop 45", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop46 ("Sweat drop 46", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop47 ("Sweat drop 47", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop48 ("Sweat drop 48", Vector) = (0,0,0,0)
        [HideInInspector] _SweatDrop49 ("Sweat drop 49", Vector) = (0,0,0,0)
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
                float4 _SweatOpacity5, _SweatOpacity6, _SweatOpacity7, _SweatOpacity8, _SweatOpacity9, _SweatOpacity10, _SweatOpacity11, _SweatOpacity12;
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
                float4 _SweatDrop20;
                float4 _SweatDrop21;
                float4 _SweatDrop22;
                float4 _SweatDrop23;
                float4 _SweatDrop24;
                float4 _SweatDrop25;
                float4 _SweatDrop26;
                float4 _SweatDrop27;
                float4 _SweatDrop28;
                float4 _SweatDrop29;
                float4 _SweatDrop30;
                float4 _SweatDrop31;
                float4 _SweatDrop32;
                float4 _SweatDrop33;
                float4 _SweatDrop34;
                float4 _SweatDrop35;
                float4 _SweatDrop36;
                float4 _SweatDrop37;
                float4 _SweatDrop38;
                float4 _SweatDrop39;
                float4 _SweatDrop40;
                float4 _SweatDrop41;
                float4 _SweatDrop42;
                float4 _SweatDrop43;
                float4 _SweatDrop44;
                float4 _SweatDrop45;
                float4 _SweatDrop46;
                float4 _SweatDrop47;
                float4 _SweatDrop48;
                float4 _SweatDrop49;
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
                if (index == 20) return _SweatDrop20;
                if (index == 21) return _SweatDrop21;
                if (index == 22) return _SweatDrop22;
                if (index == 23) return _SweatDrop23;
                if (index == 24) return _SweatDrop24;
                if (index == 25) return _SweatDrop25;
                if (index == 26) return _SweatDrop26;
                if (index == 27) return _SweatDrop27;
                if (index == 28) return _SweatDrop28;
                if (index == 29) return _SweatDrop29;
                if (index == 30) return _SweatDrop30;
                if (index == 31) return _SweatDrop31;
                if (index == 32) return _SweatDrop32;
                if (index == 33) return _SweatDrop33;
                if (index == 34) return _SweatDrop34;
                if (index == 35) return _SweatDrop35;
                if (index == 36) return _SweatDrop36;
                if (index == 37) return _SweatDrop37;
                if (index == 38) return _SweatDrop38;
                if (index == 39) return _SweatDrop39;
                if (index == 40) return _SweatDrop40;
                if (index == 41) return _SweatDrop41;
                if (index == 42) return _SweatDrop42;
                if (index == 43) return _SweatDrop43;
                if (index == 44) return _SweatDrop44;
                if (index == 45) return _SweatDrop45;
                if (index == 46) return _SweatDrop46;
                if (index == 47) return _SweatDrop47;
                if (index == 48) return _SweatDrop48;
                if (index == 49) return _SweatDrop49;
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
                [loop] for (int i = 0; i < min((int)_SweatDropCount, 50); i++)
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
                        float4 opacity = i < 4 ? _SweatOpacity0 : i < 8 ? _SweatOpacity1 : i < 12 ? _SweatOpacity2 : i < 16 ? _SweatOpacity3 : i < 20 ? _SweatOpacity4 : i < 24 ? _SweatOpacity5 : i < 28 ? _SweatOpacity6 : i < 32 ? _SweatOpacity7 : i < 36 ? _SweatOpacity8 : i < 40 ? _SweatOpacity9 : i < 44 ? _SweatOpacity10 : i < 48 ? _SweatOpacity11 : _SweatOpacity12;
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


