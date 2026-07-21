Shader "Game/RadialInvert"
{
    // 예측 정지(F) 진입: 화면 중심에서 퍼지는 원 안쪽을 맵 윤곽선 네온 그린 사이버 스페이스로 전환.
    // RadialInvertFeature(URP RendererFeature, AfterRenderingPostProcessing)의 passMaterial로 연결.
    // _Radius/_Softness는 PredictionController가 진입 진행률에 맞춰 매 프레임
    // RadialInvertFx.SetRadius로 갱신한다(Assets/_Project/View/Prediction/RadialInvertFeature.cs).
    Properties
    {
        _Radius   ("반경 (0=중심점, 1.35=전체)", Range(0, 1.5)) = 0
        _Softness ("경계 부드러움", Range(0.001, 0.3)) = 0.08
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "RadialInvert"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Radius;
            float _Softness;

            float GetLum(float2 uv)
            {
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = FragBlit(input, sampler_LinearClamp);

                if (_Radius <= 0.001)
                    return col;

                // 화면비 보정: 넓은 화면에서 원이 타원으로 찌그러지는 것을 방지
                float2 uv = input.texcoord - 0.5;
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                float dist = length(uv);

                // _Radius가 작을 때 화면 중앙에 마스크 잔여 원이 남아 떠오르지 않도록 안전 보정
                float innerRad = max(0.0, _Radius - _Softness);
                float outerRad = _Radius + _Softness;
                float mask = 1.0 - smoothstep(innerRad, outerRad, dist);
                if (_Radius < _Softness)
                {
                    mask *= saturate(_Radius / _Softness);
                }

                if (mask <= 0.001)
                    return col;

                // 1. Sobel 필터 기반 맵 전체 윤곽선(Edge Detection) 추출
                float2 texelSize = _ScreenParams.zw - 1.0;
                float2 offset = texelSize * 1.25;

                float l_tl = GetLum(input.texcoord + float2(-offset.x, -offset.y));
                float l_tr = GetLum(input.texcoord + float2( offset.x, -offset.y));
                float l_bl = GetLum(input.texcoord + float2(-offset.x,  offset.y));
                float l_br = GetLum(input.texcoord + float2( offset.x,  offset.y));
                float l_l  = GetLum(input.texcoord + float2(-offset.x,  0.0));
                float l_r  = GetLum(input.texcoord + float2( offset.x,  0.0));
                float l_t  = GetLum(input.texcoord + float2( 0.0,       -offset.y));
                float l_b  = GetLum(input.texcoord + float2( 0.0,        offset.y));

                float gx = (-1.0 * l_tl) + (1.0 * l_tr) + (-2.0 * l_l) + (2.0 * l_r) + (-1.0 * l_bl) + (1.0 * l_br);
                float gy = (-1.0 * l_tl) + (-2.0 * l_t) + (-1.0 * l_tr) + (1.0 * l_bl) + (2.0 * l_b) + (1.0 * l_br);
                float edge = saturate(sqrt(gx * gx + gy * gy) * 3.4);

                // 2. 어두운 사이버 스페이스 그리드 바탕 (Dark Cyber Grid Base)
                float centerLum = dot(col.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 cyberBase = half3(0.005, 0.035, 0.02) + centerLum * half3(0.015, 0.09, 0.045);

                // 3. 맵 윤곽선을 발광하는 네온 그린 사인으로 전환 (Neon Green Outline)
                half3 neonGreenLine = half3(0.12, 1.0, 0.45) * edge * 2.5;
                half3 cyberSpace = cyberBase + neonGreenLine;

                // 4. 충격파 링 (Shockwave Ring Pulse) & 스캔라인 효과
                float ringMask = smoothstep(_Radius - _Softness * 3.0, _Radius, dist)
                               - smoothstep(_Radius, _Radius + _Softness * 0.8, dist);
                half3 ringPulse = half3(0.25, 1.0, 0.65) * 2.2 * ringMask;

                float scanline = 0.95 + 0.05 * sin(input.texcoord.y * _ScreenParams.y * 2.0);
                cyberSpace *= scanline;

                half3 finalColor = lerp(col.rgb, cyberSpace + ringPulse, mask);
                return half4(finalColor, col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}



