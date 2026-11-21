Shader "FPS0/ShieldRim"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 0.6, 0.25, 1)
        _RimPower("Rim Power", Range(0.5, 8)) = 3
        _RimIntensity("Rim Intensity", Range(0, 5)) = 1.5
        _Fill("Fill", Range(0, 0.5)) = 0.03
        _NoiseScale("Shimmer Scale", Float) = 6
        _NoiseSpeed("Shimmer Speed", Float) = 0.6
        _NoiseStrength("Shimmer Strength", Range(0, 1)) = 0.35
        _Flash("Flash", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Rim"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _RimPower;
                half _RimIntensity;
                half _Fill;
                half _NoiseScale;
                half _NoiseSpeed;
                half _NoiseStrength;
                half _Flash;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.positionOS = v.positionOS.xyz;
                return o;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Smooth value noise for the shimmer
            float Noise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x),
                                 lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                                 lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float t = _Time.y * _NoiseSpeed;
                float shimmer = Noise(i.positionOS * _NoiseScale + float3(0, t, t * 0.7));
                float a = rim * _RimIntensity * (1.0 - _NoiseStrength + _NoiseStrength * 2.0 * shimmer) + _Fill;
                a *= 1.0 + _Flash * 2.0;
                return half4(_Color.rgb, saturate(a * _Color.a));
            }
            ENDHLSL
        }
    }
}
