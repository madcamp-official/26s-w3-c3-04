Shader "Precog/SpawnMaterialize"
{
    // 스폰 실체화 오버레이(설계 §4-⑥). 진짜 몹 위에 겹쳐 렌더되는 '디졸브 복제본' 전용.
    //  · 위에서부터 아래로 훑는 X레이 스캔선(_Reveal 0→1)
    //  · 아직 실체화 안 된 아래쪽은 프레넬 실루엣(반투명 외곽선)
    //  · 이미 지나간 위쪽은 사라져 진짜 몹이 보임
    // 발광처럼 보이도록 가산 블렌드(Blend SrcAlpha One).
    Properties
    {
        _EdgeColor ("스캔선 색(HDR)",   Color) = (3.4, 0.5, 0.35, 1)
        _SilColor  ("실루엣 색(HDR)",   Color) = (1.8, 0.18, 0.12, 1)
        _Reveal    ("실체화 진행",       Range(0,1)) = 0
        _MinY      ("메시 최소Y(로컬)",  Float) = -0.5
        _MaxY      ("메시 최대Y(로컬)",  Float) = 0.5
        _EdgeWidth ("스캔선 두께",       Range(0.01,0.4)) = 0.14
        _SilAlpha  ("실루엣 세기",       Range(0,1)) = 0.7
        _BaseFill  ("정면 채움(0=프레넬만)", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One      // 가산 — X레이 발광
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeColor;
                float4 _SilColor;
                float  _Reveal, _MinY, _MaxY, _EdgeWidth, _SilAlpha, _BaseFill;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.positionOS  = IN.positionOS.xyz;
                o.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                o.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float h  = saturate((IN.positionOS.y - _MinY) / max(1e-4, (_MaxY - _MinY))); // 0=바닥 1=위
                float rl = 1.0 - _Reveal;                     // 진행 0→1 이면 스캔선이 위(1)→아래(0)
                float d  = h - rl;                            // >0 위(이미 실체화) · <0 아래(아직)

                float edge = 1.0 - saturate(abs(d) / _EdgeWidth);   // 스캔선 강도
                edge = edge * edge;                                 // 가운데를 더 날카롭게

                float3 vd  = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(normalize(IN.normalWS), vd)), 1.4);
                float below = step(d, 0.0);                   // 아직 실체화 안 된 아래쪽

                // 미실체 고스트 = 정면 채움(_BaseFill) + 프레넬 외곽. 정면에서도 보이게.
                float ghost = below * _SilAlpha * (_BaseFill + (1.0 - _BaseFill) * fres);

                float3 col = _EdgeColor.rgb * edge * 1.6 + _SilColor.rgb * ghost;
                float  a   = saturate(edge * 1.4 + ghost);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
