Shader "Unlit/UnlitShader"
{
	Properties
	{
	// Roberta Jane 08/11/17
	// Expects global texture2DArray named _my2darray to be assigned in script.
	// via Shader.SetGlobalTexture ("_my2darray",tar);
	// Each slice for a different mosaic cell. Slice number indicated by mesh UV2.x
	// 02/28/18 add mag,aberation factors, and a viewer distance
	// for emulating a physical playback lens.
	// set magOn >0 to enable emulation.
	 _mag ("mag",Range(0.07,0.25)) = 0.19
	 _k2 ("Aberation k2",Range(-0.2,0.2)) = 0.0
	 _k3 ("Aberation k3",Range(-0.05,0.05)) = 0.0
	 _kem ("Aberation emulation",Range(-0.2,0.2)) = 0.0
	 _magOn ("Magnify On",Int) = 0
	 _viewer ("Viewer Distance Inverse",Range(-0.5,0.5)) = -0.1
	 _centripital ("Centripital Adjustment",Range(-0.5,0.5)) = -0.1
	 _untilt ("if flycam tilts in, shift this much to offset that.",Range(-0.5,0.5)) = 0.0

	}
	SubShader
	{
		Tags { "RenderType"="Opaque" }
		LOD 100

		Pass
		{
			Cull Off // two sided, to see projection through screen. Or could use Back
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma target 3.5
			
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0; // traditional UV paints the hex cell from texture
				float2 uv2 : TEXCOORD1; // use uv2 to get which slice to use (it is a textureArray)
				float2 uv3 : TEXCOORD2; // use uv3 to indicate actual position in the lens array
			};

			struct v2f
			{
				float3 uv : TEXCOORD0;
				float3 uv2 : TEXCOORD1; 
				float3 uv3 : TEXCOORD2;
				float4 vertex : SV_POSITION;
			};
			
			v2f vert (appdata v)
			{
				v2f o;
				o.vertex = UnityObjectToClipPos(v.vertex);
				o.uv.xy = v.uv.xy;
				o.uv.z = v.uv2.x;							// expecting slice number in uv2. Frag will take it in z
				o.uv3.xy = v.uv3.xy;
				return o;
			}

			int _magOn;
			float _mag;
			float _k2;
			float _k3;
			float _kem;
			float _viewer;
			float _centripital;
			float _untilt;

			UNITY_DECLARE_TEX2DARRAY(_my2darray);
			fixed4 frag (v2f i) : SV_Target
			{
				float2 rv;	// radial position in this cell
				float r;

				float2 arv; // radial position on lens

				float me; // mag effective

				float cei;	// inverse of centripital or viewer distance;

				float k2e;
				float k3e;

				if (_magOn < 1)  { // 0, flag I am making mosaic for behind a lens
					me = 1.0 - 0.5 * _k2 - 0.25 * _k3;
					k2e = _k2;
					k3e = _k3;
					cei = _centripital + _untilt;
				} else {			// 1, flag I am pretending to have a lens array in front of mosaic
					k2e = _kem;
					k3e = 0.0;
					me = _mag - 0.5 * k2e;
				    cei = _viewer + _centripital + _untilt;	// it still has the centripital tho. so add it in.
				}

				rv.x = i.uv.x - 0.5; // rx;
			    rv.y = i.uv.y - 0.5; // ry;
				r = length(rv);

				arv.x = i.uv3.x - 0.5;
				arv.y = i.uv3.y - 0.5;

				i.uv.x = 0.5 + (rv.x) * me + r * rv.x * k2e + r * r * rv.x * k3e + arv.x * cei;
				i.uv.y = 0.5 + (rv.y) * me + r * rv.y * k2e + r * r * rv.y * k3e + arv.y * cei;
				fixed4 col =  UNITY_SAMPLE_TEX2DARRAY(_my2darray, i.uv);
				return col;
			}
			ENDCG
		}
	}
}
