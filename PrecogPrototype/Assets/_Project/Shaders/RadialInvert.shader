Shader "Game/RadialInvert"
{
    // 예측 정지(F) 진입: 화면 중심에서 퍼지는 원 안쪽을 강렬한 사이버펑크 산데비스탄 그린 모노크롬 톤으로 전환.
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

                // 1. 휘도 (Luminance) 계산
                float lum = dot(col.rgb, half3(0.2126, 0.7152, 0.0722));

                // 2. 맵 구조물(벽, 계단, 엄폐물, 텍스처 명암) 윤곽선을 100% 선명하게 보존하는 산데비스탄 톤
                // 원본 씬의 음영 텍스처(col.rgb)를 살려 형태감을 유지하면서 에메랄드 그린 톤으로 컬러 그레이딩
                half3 structureShaded = col.rgb * half3(0.35, 1.20, 0.60);
                half3 emeraldShadows   = half3(0.01, 0.09, 0.05);
                half3 cyberHighlights = half3(0.08, 0.85, 0.42);

                half3 colorGraded = lerp(emeraldShadows, cyberHighlights, saturate(lum * 1.25));
                half3 tinted = lerp(structureShaded, colorGraded, 0.55);

                // 3. 외곽 충격파 링 (Shockwave Ring Pulse) & 비네팅 스캔라인
                float ringMask = smoothstep(_Radius - _Softness * 3.0, _Radius, dist)
                               - smoothstep(_Radius, _Radius + _Softness * 0.8, dist);
                half3 ringPulse = half3(0.25, 1.0, 0.65) * 2.0 * ringMask;

                float scanline = 0.96 + 0.04 * sin(input.texcoord.y * _ScreenParams.y * 1.8);
                float vig = 1.0 - smoothstep(0.4, 0.9, dist);
                tinted *= scanline * lerp(0.7, 1.0, vig);

                half3 finalColor = lerp(col.rgb, tinted + ringPulse, mask);
                return half4(finalColor, col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}


