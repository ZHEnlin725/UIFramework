Shader "UIFramework/GrabPassBlur"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Tint Color (RGB)", 2D) = "white" {}
        _Size ("Size", Range(0, 20)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Opaque"
        }

        CGINCLUDE
        #include "UnityCG.cginc"

        struct v2f_blur
        {
            float4 vertex : POSITION;
            float4 uvgrab : TEXCOORD0;
        };

        v2f_blur vert_blur(appdata_img v)
        {
            v2f_blur o;
            o.vertex = UnityObjectToClipPos(v.vertex);
            #if UNITY_UV_STARTS_AT_TOP
            float scale = -1.0;
            #else
            float scale = 1.0;
            #endif
            o.uvgrab.xy = (float2(o.vertex.x, o.vertex.y * scale) + o.vertex.w) * 0.5;
            o.uvgrab.zw = o.vertex.zw;
            return o;
        }

        float _Size;
        sampler2D _GrabTexture;
        float4 _GrabTexture_TexelSize;

        half4 blur_vertical(v2f_blur i, float weight, float kernely)
        {
            return tex2Dproj(_GrabTexture,
                             UNITY_PROJ_COORD(
                                 float4(i.uvgrab.x, i.uvgrab.y + _GrabTexture_TexelSize.y * kernely * _Size, i.
                                     uvgrab.
                                     z, i.uvgrab.w))) * weight;
        }

        half4 blur_horizontal(v2f_blur i, float weight, float kernelx)
        {
            return tex2Dproj(_GrabTexture,
                             UNITY_PROJ_COORD(
                                 float4(i.uvgrab.x + _GrabTexture_TexelSize.x * kernelx * _Size, i.uvgrab.y, i.
                                     uvgrab.
                                     z, i.uvgrab.w))) * weight;
        }

        half4 frag_vertical_blur(v2f_blur i) : COLOR
        {
            half4 sum = half4(0, 0, 0, 0);
            sum += blur_vertical(i, 0.026, -4);
            sum += blur_vertical(i, 0.035, -3.5);
            sum += blur_vertical(i, 0.044, -3);
            sum += blur_vertical(i, 0.052, -2.5);
            sum += blur_vertical(i, 0.061, -2);
            sum += blur_vertical(i, 0.069, -1.5);
            sum += blur_vertical(i, 0.078, -1);
            sum += blur_vertical(i, 0.087, -0.5);
            sum += blur_vertical(i, 0.095, 0);
            sum += blur_vertical(i, 0.087, 0.5);
            sum += blur_vertical(i, 0.078, 1);
            sum += blur_vertical(i, 0.069, 1.5);
            sum += blur_vertical(i, 0.061, 2);
            sum += blur_vertical(i, 0.052, 2.5);
            sum += blur_vertical(i, 0.044, 3);
            sum += blur_vertical(i, 0.035, 3.5);
            sum += blur_vertical(i, 0.026, 4);
            return sum;
        }

        half4 frag_horizontal_blur(v2f_blur i) : COLOR
        {
            half4 sum = half4(0, 0, 0, 0);
            sum += blur_horizontal(i, 0.026, -4);
            sum += blur_horizontal(i, 0.035, -3.5);
            sum += blur_horizontal(i, 0.044, -3);
            sum += blur_horizontal(i, 0.052, -2.5);
            sum += blur_horizontal(i, 0.061, -2);
            sum += blur_horizontal(i, 0.069, -1.5);
            sum += blur_horizontal(i, 0.078, -1);
            sum += blur_horizontal(i, 0.087, -0.5);
            sum += blur_horizontal(i, 0.095, 0);
            sum += blur_horizontal(i, 0.087, 0.5);
            sum += blur_horizontal(i, 0.078, 1);
            sum += blur_horizontal(i, 0.069, 1.5);
            sum += blur_horizontal(i, 0.061, 2);
            sum += blur_horizontal(i, 0.052, 2.5);
            sum += blur_horizontal(i, 0.044, 3);
            sum += blur_horizontal(i, 0.035, 3.5);
            sum += blur_horizontal(i, 0.026, 4);
            return sum;
        }
        ENDCG

        Zwrite Off

        // Vertical blur
        GrabPass
        {
            Tags
            {
                "LightMode" = "Always"
            }
        }
        Pass
        {
            Name "Vertical Blur Pass"
            Tags
            {
                "LightMode" = "Always"
            }

            CGPROGRAM
            #pragma vertex vert_blur
            #pragma fragment frag_vertical_blur
            #pragma fragmentoption ARB_precision_hint_fastest
            ENDCG
        }

        // Horizontal blur
        GrabPass
        {
            Tags
            {
                "LightMode" = "Always"
            }
        }
        Pass
        {
            Name "Horizontal Blur Pass"

            Tags
            {
                "LightMode" = "Always"
            }

            CGPROGRAM
            #pragma vertex vert_blur
            #pragma fragment frag_horizontal_blur
            #pragma fragmentoption ARB_precision_hint_fastest
            ENDCG
        }

        // Blend
        GrabPass
        {
            Tags
            {
                "LightMode" = "Always"
            }
        }
        Pass
        {
            Name "Blend Pass"
            Tags
            {
                "LightMode" = "Always"
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma fragmentoption ARB_precision_hint_fastest
            #include "UnityCG.cginc"

            struct v2f
            {
                float4 vertex : POSITION;
                float4 uvgrab : TEXCOORD0;
                float2 uvmain : TEXCOORD1;
            };

            float4 _MainTex_ST;

            v2f vert(appdata_img v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                #if UNITY_UV_STARTS_AT_TOP
                float scale = -1.0;
                #else
                float scale = 1.0;
                #endif
                o.uvgrab.xy = (float2(o.vertex.x, o.vertex.y * scale) + o.vertex.w) * 0.5;
                o.uvgrab.zw = o.vertex.zw;
                o.uvmain = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 _Color;
            sampler2D _MainTex;

            half4 frag(v2f i) : COLOR
            {
                float2 offset = _GrabTexture_TexelSize.xy;
                i.uvgrab.xy = offset * i.uvgrab.z + i.uvgrab.xy;

                half4 col = tex2Dproj(_GrabTexture, UNITY_PROJ_COORD(i.uvgrab));
                half4 tint = tex2D(_MainTex, i.uvmain) * _Color;

                return col * tint;
            }
            ENDCG
        }
    }
}