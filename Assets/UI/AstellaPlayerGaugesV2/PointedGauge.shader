Shader "Astella/UI/PointedGauge"
{
    Properties
    {
        [PerRendererData] _MainTex ("Full Fill Sprite", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Fill ("Current / Maximum", Range(0,1)) = 1
        _FillBounds ("Fill Bounds / XY Minimum, ZW Size", Vector) = (0.009,0.21875,0.982,0.5625)
        _SpriteUV ("Sprite Atlas UV / XY Minimum, ZW Size", Vector) = (0,0,1,1)
        _PointWidth ("Point Width / Full Sprite", Float) = 0.012
        _TipTex ("Moving Tip Highlight", 2D) = "black" {}
        _TipUV ("Tip Texture UV / XY Minimum, ZW Size", Vector) = (0,0,1,1)
        _TipPadding ("Highlight Padding / XY", Vector) = (0.001,0.015625,0,0)
        _TipColor ("Tip Highlight Tint", Color) = (1,1,1,1)
        _TipStrength ("Tip Highlight Strength", Range(0,1)) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                half4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                half4 mask : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            sampler2D _MainTex, _TipTex;
            float4 _MainTex_ST;
            half4 _Color, _TextureSampleAdd, _TipColor;
            float _Fill, _PointWidth, _TipStrength;
            float4 _FillBounds, _SpriteUV, _TipUV, _TipPadding, _ClipRect;
            float _UIMaskSoftnessX, _UIMaskSoftnessY;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;
                float2 pixelSize = output.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = half4(input.vertex.xy * 2 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return output;
            }

            half4 frag(v2f input) : SV_Target
            {
                float ratio = saturate(_Fill);
                half4 source = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                float2 localUV = (input.texcoord - _SpriteUV.xy) / max(_SpriteUV.zw, float2(0.00001, 0.00001));
                float left = _FillBounds.x;
                float front = left + _FillBounds.z * ratio;
                float halfHeight = max(_FillBounds.w * 0.5, 0.00001);
                float centerY = _FillBounds.y + halfHeight;
                // Below 12 native pixels, shrink the triangle instead of drawing outside the left pointed end.
                float tipWidth = min(max(_PointWidth, 0), max(front - left, 0));
                float edge = front - tipWidth * saturate(abs(localUV.y - centerY) / halfHeight);
                float mask = step(localUV.x, edge);
                // At full, use the full original textured shape, including its original right point.
                mask = lerp(mask, 1, step(0.999999, ratio));
                mask *= step(0.000001, ratio);

                float2 highlightSize = float2(max(tipWidth + _TipPadding.x * 2, 0.00001),
                    max(_FillBounds.w + _TipPadding.y * 2, 0.00001));
                float2 highlightMin = float2(front - tipWidth - _TipPadding.x, _FillBounds.y - _TipPadding.y);
                float2 highlightUV = (localUV - highlightMin) / highlightSize;
                float inHighlight = step(0, highlightUV.x) * step(highlightUV.x, 1) * step(0, highlightUV.y) * step(highlightUV.y, 1);
                half4 highlight = tex2D(_TipTex, _TipUV.xy + saturate(highlightUV) * _TipUV.zw) * _TipColor;
                float partial = step(0.000001, ratio) * (1 - step(0.999999, ratio));
                // Clip the highlight to the original full inner shape as well, especially near zero.
                half tipAlpha = highlight.a * saturate(_TipStrength) * inHighlight * partial * source.a;
                half baseAlpha = source.a * mask;
                half alpha = tipAlpha + baseAlpha * (1 - tipAlpha);
                half3 rgb = (highlight.rgb * tipAlpha + source.rgb * baseAlpha * (1 - tipAlpha)) / max(alpha, 0.00001);
                half4 color = half4(rgb * input.color.rgb, alpha * input.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                    half2 clipMask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    color.a *= clipMask.x * clipMask.y;
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                    clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}
