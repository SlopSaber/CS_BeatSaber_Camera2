Shader "Custom/VolumetricBlit" {
	Properties {
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader {
		ColorMask RGB
		Pass {
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.5
			#pragma multi_compile_instancing
			#pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

			#include "UnityCG.cginc"

			sampler2D _MainTex;
			half4 _MainTex_ST;

			struct v2f {
				float2 uv : TEXCOORD0;
				float4 vertex : SV_POSITION;

				UNITY_VERTEX_OUTPUT_STEREO
			};

			v2f vert(appdata_base v) {
				v2f o;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);

				return o;
			}

			fixed4 frag(v2f i, bool frontFace : SV_IsFrontFace) : SV_Target {
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
				// Both faces must share one pass; SRP selects one forward pass per renderer.
				if(!frontFace) i.uv.x = 1 - i.uv.x;
				return tex2D(_MainTex, i.uv);
			}
			ENDCG
		}
	}
}
