Shader "FPS0/StatusOverlay"
{
    Properties
    {
        [KeywordEnum(Rim, Creep, Crack, Arc)] _Mode("Mode", Float) = 0
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        [HDR] _EdgeColor("Edge Color", Color) = (1, 1, 1, 1)
        _Intensity("Intensity", Range(0, 1)) = 1
        _Fill("Fill", Range(0, 1)) = 0.3
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _RimStrength("Rim Strength", Range(0, 4)) = 1
        _NoiseScale("Noise Scale", Float) = 12
        _NoiseSpeed("Noise Speed", Float) = 0.3
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.4
        _PulseSpeed("Pulse Speed", Float) = 0
        _PulseAmount("Pulse Amount", Range(0, 1)) = 0
        _Flicker("Flicker", Range(0, 1)) = 0
        _FlickerRate("Flicker Per Second", Float) = 24
        _Sparkle("Sparkle", Range(0, 4)) = 0
        _CreepMin("Creep Min", Range(0, 1.2)) = 0.15
        _CreepMax("Creep Max", Range(0, 1.2)) = 0.85
        _CreepEdge("Creep Edge (m)", Range(0.01, 0.5)) = 0.12
        _CrackScale("Crack Scale", Float) = 8
        _CrackWidth("Crack Width", Range(0.005, 0.2)) = 0.04
        _ArcRate("Arc Cycles Per Second", Float) = 0.9
        _ArcCoverage("Arc Lit Share", Range(0, 1)) = 0.35
        _Fade("Fade", Range(0, 1)) = 1
        _Push("Surface Push (m)", Range(0, 0.02)) = 0.004
        _BaseY("Base Y", Float) = 0
        _Height("Height", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Overlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _MODE_RIM _MODE_CREEP _MODE_CRACK _MODE_ARC
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _EdgeColor;
                half _Intensity;
                half _Fill;
                half _RimPower;
                half _RimStrength;
                half _NoiseScale;
                half _NoiseSpeed;
                half _NoiseStrength;
                half _PulseSpeed;
                half _PulseAmount;
                half _Flicker;
                half _FlickerRate;
                half _Sparkle;
                half _CreepMin;
                half _CreepMax;
                half _CreepEdge;
                half _CrackScale;
                half _CrackWidth;
                half _ArcRate;
                half _ArcCoverage;
                half _Fade;
                half _Push;
                float _BaseY;
                float _Height;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 pos = v.positionOS.xyz + v.normalOS * _Push;
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(p.positionWS);
                o.uv = v.uv;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth 2D value noise on the mesh UVs, so patterns stick to the skin
            float Noise(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x),
                            lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            // Voronoi: distance to the nearest cell edge, and that cell's random id
            float2 Cells(float2 x)
            {
                float2 i = floor(x);
                float2 f = frac(x);
                float d1 = 8.0, d2 = 8.0, id = 0.0;
                for (int y = -1; y <= 1; y++)
                for (int k = -1; k <= 1; k++)
                {
                    float2 g = float2(k, y);
                    float2 o = float2(Hash21(i + g), Hash21(i + g + 17.0));
                    float d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; id = Hash21(i + g + 31.0); }
                    else if (d < d2) d2 = d;
                }
                return float2(d2 - d1, id);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(i.viewWS);
                float rim = pow(1.0 - saturate(dot(n, v)), _RimPower);
                float t = _Time.y;
                float noise = Noise(i.uv * _NoiseScale + float2(t * _NoiseSpeed, t * _NoiseSpeed * 0.7));
                float shade = 1.0 - _NoiseStrength + _NoiseStrength * 2.0 * noise;
                float pulse = 1.0 + _PulseAmount * sin(t * _PulseSpeed * 6.2832);
                float seed = frac(dot(unity_ObjectToWorld._m03_m13_m23, float3(0.13, 0.71, 0.37)));
                float flick = lerp(1.0, step(0.3, Hash21(float2(floor(t * _FlickerRate), seed * 97.0))), _Flicker);
                float3 col = _Color.rgb;
                float a = 0.0;

            #if defined(_MODE_CREEP)
                float level = _BaseY + _Height * lerp(_CreepMin, _CreepMax, _Intensity);
                float jag = (noise - 0.5) * _CreepEdge * 2.0;
                float d = level + jag - i.positionWS.y;
                float cover = smoothstep(-_CreepEdge * 0.5, _CreepEdge * 0.5, d);
                float edge = cover * (1.0 - smoothstep(0.0, _CreepEdge, d));
                float sparkle = step(0.985, Hash21(floor(i.uv * 220.0))) * (0.5 + 0.5 * sin(t * 6.0 + Hash21(floor(i.uv * 220.0)) * 40.0));
                col = lerp(col, _EdgeColor.rgb, edge);
                a = cover * (_Fill * shade + rim * _RimStrength + sparkle * _Sparkle) + edge * 0.6;
            #elif defined(_MODE_CRACK)
                float2 c = Cells(i.uv * _CrackScale);
                float crackLine = 1.0 - smoothstep(0.0, _CrackWidth, c.x);
                float shown = step(c.y, _Intensity * 1.05);
                col = lerp(col, _EdgeColor.rgb, crackLine);
                a = crackLine * shown + rim * _RimStrength * 0.3 * _Intensity + _Fill * _Intensity * shade;
            #elif defined(_MODE_ARC)
                // Veins on a slowly drifting cell grid; each cell flares on its own cycle (quick rise, slower fade) and skips some cycles at random.
                float2 warp = float2(Noise(i.uv * _NoiseScale * 2.0 + t * _NoiseSpeed), Noise(i.uv * _NoiseScale * 2.0 - t * _NoiseSpeed)) - 0.5;
                float2 c = Cells(i.uv * _CrackScale + warp * 0.8 + seed * 37.0);
                float vein = 1.0 - smoothstep(0.0, _CrackWidth, c.x);
                float rate = _ArcRate * (0.6 + 0.8 * Hash21(float2(c.y * 53.0, 7.0)));
                float cyc = t * rate + c.y * 7.0;
                float p = frac(cyc);
                float lit = smoothstep(0.0, 0.06, p) * (1.0 - smoothstep(0.06, max(0.07, _ArcCoverage), p));
                lit *= step(0.4, Hash21(float2(c.y * 17.0, floor(cyc))));
                col = lerp(col, _EdgeColor.rgb, vein * lit);
                a = (vein * lit * 1.5 + rim * _RimStrength * shade + _Fill * shade) * _Intensity;
            #else
                a = (rim * _RimStrength * shade + _Fill * shade) * _Intensity;
            #endif

                a *= pulse * flick * _Color.a * _Fade;
                return half4(col, saturate(a));
            }
            ENDHLSL
        }
    }
}
