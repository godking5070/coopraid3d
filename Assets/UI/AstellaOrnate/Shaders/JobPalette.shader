Shader "Astella/UI/JobPalette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ShadowColor ("Palette / Shadow", Color) = (0.025, 0.035, 0.07, 1)
        _MidColor ("Palette / Midtone", Color) = (0.70, 0.45, 0.13, 1)
        _HighlightColor ("Palette / Highlight", Color) = (1, 0.96, 0.78, 1)
        _MidPoint ("Palette / Luminance Midpoint", Range(0.1, 0.9)) = 0.38

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
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "JobPalette"

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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;
            half4 _TextureSampleAdd;
            half4 _ShadowColor;
            half4 _MidColor;
            half4 _HighlightColor;
            half _MidPoint;
            float4 _ClipRect;
            float _UIMaskSoftnessX;
            float _UIMaskSoftnessY;

            v2f vert(appdata_t input)
            {
                v2f output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = TRANSFORM_TEX(input.texcoord, _MainTex);
                output.color = input.color * _Color;

                // Match uGUI's local-space RectMask2D clipping, including soft edges.
                float2 pixelSize = output.vertex.w;
                pixelSize /= abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float4 clipRect = clamp(_ClipRect, -2e10, 2e10);
                output.mask = half4(input.vertex.xy * 2 - clipRect.xy - clipRect.zw,
                    0.25 / (0.25 * half2(_UIMaskSoftnessX, _UIMaskSoftnessY) + abs(pixelSize.xy)));
                return output;
            }

            half4 frag(v2f input) : SV_Target
            {
                half4 sampleColor = tex2D(_MainTex, input.texcoord) + _TextureSampleAdd;
                // The illustration's luminance drives the ramp, retaining linework and light/shadow detail.
                // Texture hue is deliberately discarded so different original spell hues share one job palette.
                half luminance = saturate(dot(sampleColor.rgb, half3(0.2126, 0.7152, 0.0722)));
                half midpoint = clamp(_MidPoint, 0.1, 0.9);
                half3 lower = lerp(_ShadowColor.rgb, _MidColor.rgb, saturate(luminance / midpoint));
                half3 upper = lerp(_MidColor.rgb, _HighlightColor.rgb,
                    saturate((luminance - midpoint) / (1 - midpoint)));
                half3 paletteColor = lerp(lower, upper, step(midpoint, luminance));

                // Palette alpha never replaces artwork transparency; Graphic and CanvasGroup alpha still work.
                half4 color = half4(paletteColor * input.color.rgb, sampleColor.a * input.color.a);
                #ifdef UNITY_UI_CLIP_RECT
                    half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) * input.mask.zw);
                    color.a *= mask.x * mask.y;
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
