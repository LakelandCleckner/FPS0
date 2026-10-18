Shader "FPS0/WeaponZones"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Smoothness("Smoothness", Range(0, 1)) = 0
        [Toggle(_NORMALMAP)] _UseNormalMap("Use Normal Map", Float) = 0
        [Normal][NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        [ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1
        [ToggleOff] _EnvironmentReflections("Environment Reflections", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2

        [Header(Zones)]
        [NoScaleOffset] _ZoneMask("Zone Mask (RGBA = zones 1-4)", 2D) = "black" {}

        [Header(Zone 1 Red)]
        _Z1Color("Zone 1 Color", Color) = (1, 1, 1, 1)
        _Z1Strength("Zone 1 Recolor", Range(0, 1)) = 0
        _Z1Brightness("Zone 1 Brightness", Range(0, 4)) = 1
        _Z1Metallic("Zone 1 Metallic", Range(0, 1)) = 0
        _Z1Smoothness("Zone 1 Smoothness", Range(0, 1)) = 0
        [HDR] _Z1Emission("Zone 1 Emission", Color) = (0, 0, 0, 1)

        [Header(Zone 2 Green)]
        _Z2Color("Zone 2 Color", Color) = (1, 1, 1, 1)
        _Z2Strength("Zone 2 Recolor", Range(0, 1)) = 0
        _Z2Brightness("Zone 2 Brightness", Range(0, 4)) = 1
        _Z2Metallic("Zone 2 Metallic", Range(0, 1)) = 0
        _Z2Smoothness("Zone 2 Smoothness", Range(0, 1)) = 0
        [HDR] _Z2Emission("Zone 2 Emission", Color) = (0, 0, 0, 1)

        [Header(Zone 3 Blue)]
        _Z3Color("Zone 3 Color", Color) = (1, 1, 1, 1)
        _Z3Strength("Zone 3 Recolor", Range(0, 1)) = 0
        _Z3Brightness("Zone 3 Brightness", Range(0, 4)) = 1
        _Z3Metallic("Zone 3 Metallic", Range(0, 1)) = 0
        _Z3Smoothness("Zone 3 Smoothness", Range(0, 1)) = 0
        [HDR] _Z3Emission("Zone 3 Emission", Color) = (0, 0, 0, 1)

        [Header(Zone 4 Alpha)]
        _Z4Color("Zone 4 Color", Color) = (1, 1, 1, 1)
        _Z4Strength("Zone 4 Recolor", Range(0, 1)) = 0
        _Z4Brightness("Zone 4 Brightness", Range(0, 4)) = 1
        _Z4Metallic("Zone 4 Metallic", Range(0, 1)) = 0
        _Z4Smoothness("Zone 4 Smoothness", Range(0, 1)) = 0
        [HDR] _Z4Emission("Zone 4 Emission", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);
        SAMPLER(sampler_BumpMap);
        TEXTURE2D(_ZoneMask);
        SAMPLER(sampler_ZonePointClamp);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _Metallic;
            half _Smoothness;
            half _BumpScale;
            half4 _Z1Color, _Z2Color, _Z3Color, _Z4Color;
            half4 _Z1Emission, _Z2Emission, _Z3Emission, _Z4Emission;
            half _Z1Strength, _Z2Strength, _Z3Strength, _Z4Strength;
            half _Z1Brightness, _Z2Brightness, _Z3Brightness, _Z4Brightness;
            half _Z1Metallic, _Z2Metallic, _Z3Metallic, _Z4Metallic;
            half _Z1Smoothness, _Z2Smoothness, _Z3Smoothness, _Z4Smoothness;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile _ _FORWARD_PLUS _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;          // xy base map, zw raw (mask)
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 fogVertex : TEXCOORD4;    // x fog, yzw vertex lighting
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.uv = float4(TRANSFORM_TEX(input.uv, _BaseMap), input.uv);
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                half3 vertexLight = 0;
                #ifdef _ADDITIONAL_LIGHTS_VERTEX
                    vertexLight = VertexLighting(p.positionWS, n.normalWS);
                #endif
                o.fogVertex = half4(ComputeFogFactor(p.positionCS.z), vertexLight);
                return o;
            }

            void ApplyZone(half w, half4 color, half strength, half brightness, half metal, half smooth,
                           half4 emis, half lum, inout half3 albedo, inout half metallic,
                           inout half smoothness, inout half3 emission)
            {
                albedo = lerp(albedo, color.rgb * lum * brightness, w * strength);
                metallic = lerp(metallic, metal, w);
                smoothness = lerp(smoothness, smooth, w);
                emission += emis.rgb * w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy) * _BaseColor;
                half4 mask = SAMPLE_TEXTURE2D(_ZoneMask, sampler_ZonePointClamp, input.uv.zw);
                half lum = dot(baseCol.rgb, half3(0.299h, 0.587h, 0.114h));

                half3 albedo = baseCol.rgb;
                half metallic = _Metallic;
                half smoothness = _Smoothness;
                half3 emission = 0;

                ApplyZone(mask.r, _Z1Color, _Z1Strength, _Z1Brightness, _Z1Metallic, _Z1Smoothness, _Z1Emission, lum, albedo, metallic, smoothness, emission);
                ApplyZone(mask.g, _Z2Color, _Z2Strength, _Z2Brightness, _Z2Metallic, _Z2Smoothness, _Z2Emission, lum, albedo, metallic, smoothness, emission);
                ApplyZone(mask.b, _Z3Color, _Z3Strength, _Z3Brightness, _Z3Metallic, _Z3Smoothness, _Z3Emission, lum, albedo, metallic, smoothness, emission);
                ApplyZone(mask.a, _Z4Color, _Z4Strength, _Z4Brightness, _Z4Metallic, _Z4Smoothness, _Z4Emission, lum, albedo, metallic, smoothness, emission);

                float3 normalWS = input.normalWS;
                #ifdef _NORMALMAP
                    half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.xy), _BumpScale);
                    float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                    normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS));
                #endif
                normalWS = NormalizeNormalPerPixel(normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogVertex.x;
                inputData.vertexLighting = input.fogVertex.yzw;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = metallic;
                surface.smoothness = smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.emission = emission;
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(SAttr input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return positionCS;
            }

            half4 ShadowFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct DAttr
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(DAttr input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing

            struct NAttr
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct NVary
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            NVary NormalsVert(NAttr input)
            {
                NVary o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 NormalsFrag(NVary input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
