// 겐지 용검식 베기 이펙트 — 칼이 3D를 훑고 지나간 "곡면(원뿔 단면)"에 그린다.
//
// 메시 규약(SwordSlash.cs):
//   uv.x : 0=스윙 시작, 1=스윙 끝(선단)
//   uv.y : 0=안쪽(손잡이 반경), 1=바깥(칼끝 반경)
//
// 색: 안쪽 녹색 → 바깥으로 갈수록 노랑 → 최외곽 흰 테두리.
// 결: 반경 방향 줄무늬(streak)가 스윙 방향으로 변주되어 너울거리는 에너지 느낌.
Shader "Precog/SwordSlash"
{
    Properties
    {
        [HDR]_CoreColor ("최외곽 테두리(흰)", Color) = (3.0,3.0,2.4,1)
        [HDR]_MidColor  ("중간(노랑)",        Color) = (1.8,1.25,0.06,1)
        [HDR]_EdgeColor ("안쪽(녹색)",        Color) = (0.25,1.5,0.15,1)

        _RimStart  ("노랑 시작(반경)", Range(0,1)) = 0.45
        _CoreStart ("흰 테두리 시작",  Range(0,1)) = 0.88
        _InnerFade ("안쪽 페이드",     Range(0,1)) = 0.35

        _StreakFreq ("줄무늬 빈도", Range(0,80)) = 26
        _StreakAmt  ("줄무늬 세기", Range(0,1))  = 0.45

        _Reveal     ("그어짐 진행도",  Range(0,1)) = 1
        _RevealSoft ("선단 부드러움",  Range(0.001,1)) = 0.10
        _TailFade   ("꼬리 페이드",    Range(0,1)) = 0.45
        _Fade       ("전체 페이드",    Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Slash"
            Blend One One      // 가산
            ZWrite Off
            Cull Off           // 곡면이라 양면 보여야 함
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _MidColor, _EdgeColor;
                float  _RimStart, _CoreStart, _InnerFade;
                float  _StreakFreq, _StreakAmt;
                float  _Reveal, _RevealSoft, _TailFade, _Fade;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv    = IN.uv;
                o.color = IN.color;
                return o;
            }

            float Hash(float n) { return frac(sin(n) * 43758.5453); }

            half4 frag(Varyings IN) : SV_Target
            {
                float u = IN.uv.x;    // 스윙 방향
                float v = IN.uv.y;    // 반경(0=안, 1=바깥)

                // 색: 녹색 → 노랑 → 흰 테두리
                float3 col = lerp(_EdgeColor.rgb, _MidColor.rgb,  smoothstep(_RimStart, _CoreStart, v));
                col        = lerp(col,            _CoreColor.rgb, smoothstep(_CoreStart, 1.0, v));

                // 안쪽은 옅게(손잡이 쪽이 흐려짐), 바깥 끝은 살짝 부드럽게
                float radial = smoothstep(0.0, max(_InnerFade, 0.001), v) * (1.0 - smoothstep(0.97, 1.0, v));

                // 반경 방향 줄무늬 — 스윙 방향으로 흩어져 너울거림
                float s1 = sin(u * _StreakFreq + v * 2.3);
                float s2 = sin(u * _StreakFreq * 0.47 + 1.7);
                float streak = 1.0 - _StreakAmt * (0.5 - 0.5 * (s1 * 0.65 + s2 * 0.35));

                // 스윙 진행: 선단(_Reveal)보다 앞은 아직 안 나타남
                float rev  = saturate((_Reveal - u) / _RevealSoft);
                // 꼬리(시작쪽)는 서서히 사라짐
                float tail = smoothstep(0.0, max(_TailFade, 0.001), _Reveal - u);

                float a = radial * streak * rev * tail * _Fade * IN.color.a;
                return half4(col * a, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
