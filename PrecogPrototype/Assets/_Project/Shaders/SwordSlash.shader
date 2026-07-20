// 겐지 용검식 베기 이펙트.
// 폭 방향(uv.y)으로 흰 코어 → 노랑 → 녹색 3단 그라디언트, 가산 합성.
// 길이 방향(uv.x)으로 _Reveal이 훑고 지나가며 "그어진다".
//
// 메시 규약(SwordSlash.cs가 생성):
//   uv.x : 0=두꺼운 시작쪽, 1=뾰족한 끝
//   uv.y : 0/1=바깥 가장자리, 0.5=중심선(코어)
Shader "Precog/SwordSlash"
{
    Properties
    {
        [HDR]_CoreColor ("코어(중심) 색", Color) = (10,10,9,1)
        [HDR]_MidColor  ("중간 색",       Color) = (6,5.2,0.9,1)
        [HDR]_EdgeColor ("가장자리 색",    Color) = (1.4,3.2,0.5,1)

        _CoreSize ("코어 폭",     Range(0,1)) = 0.14
        _MidSize  ("중간 폭",     Range(0,1)) = 0.46
        _EdgeSoft ("가장자리 감쇠", Range(0,1)) = 0.30

        _Reveal     ("그어짐 진행도", Range(0,1)) = 1
        _RevealSoft ("선단 부드러움", Range(0.001,1)) = 0.12
        _TailFade   ("꼬리 페이드",   Range(0,1)) = 0.25
        _Fade       ("전체 페이드",   Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Slash"
            Blend One One          // 가산
            ZWrite Off
            Cull Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _MidColor, _EdgeColor;
                float  _CoreSize, _MidSize, _EdgeSoft;
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

            half4 frag(Varyings IN) : SV_Target
            {
                // 중심선에서의 거리(0=중심, 1=가장자리)
                float d = abs(IN.uv.y - 0.5) * 2.0;

                // 흰 코어 → 노랑 → 녹색
                float3 col = lerp(_CoreColor.rgb, _MidColor.rgb,  smoothstep(_CoreSize, _MidSize, d));
                col        = lerp(col,            _EdgeColor.rgb, smoothstep(_MidSize,  1.0,      d));

                // 가장자리로 갈수록 사라짐(부드러운 경계)
                float falloff = 1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, d);

                // 길이 방향으로 그어짐: 선단(_Reveal)보다 앞은 아직 안 나타남
                float rev = saturate((_Reveal - IN.uv.x) / _RevealSoft);
                // 꼬리(시작쪽)는 서서히 옅어짐
                float tail = smoothstep(0.0, max(_TailFade, 0.001), _Reveal - IN.uv.x);

                float a = falloff * rev * lerp(1.0, tail, step(0.001, _TailFade)) * _Fade * IN.color.a;
                return half4(col * a, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
