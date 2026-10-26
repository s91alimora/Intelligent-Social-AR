Shader "iXR/Occlusion Unlit Vertex Color"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
        _DepthBias ("Depth Bias", Float) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION

            #include "UnityCG.cginc"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/BiRP/EnvironmentOcclusionBiRP.cginc"

            fixed4 _Color;
            float _DepthBias;

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                META_DEPTH_VERTEX_OUTPUT(0)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.color.rgb *= o.color.a; // premultiplied, like Sprites/Default
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, v.vertex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 col = i.color;
                // Bias pulls the lines slightly toward the camera so the real floor doesn't hide them.
                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(i, col, _DepthBias);
                return col;
            }
            ENDCG
        }
    }
}