Shader "Custom/CompositeLightmap" {
  Properties {
    _MainTex ("Albedo (RGB)", 2D) = "white" {}
    _LightMaps("Lightmap Array", 2DArray) = "white" {}
    _LightmapCount("Lightmap Count", int) = 0
    _LightmapScaleOffset("Lightmap Scale Offset", Vector) = (1, 1, 0, 0)
  }
  SubShader {
    Tags { "RenderType"="Opaque" }
    LOD 200

    CGPROGRAM
    // Physically based Standard lighting model, and enable shadows on all light types
    #pragma surface surf Standard fullforwardshadows

    // Use shader model 3.0 target, to get nicer looking lighting
    #pragma target 3.0

    sampler2D _MainTex;
    UNITY_DECLARE_TEX2DARRAY(_LightMaps);
    int _LightmapCount;
    float4 _LightColors[64];
    float4 _LightmapScaleOffset;

    struct Input {
      float2 uv_MainTex;
      float2 uv3_wat; // Use uv2 for lightmap coordinates
    };

    void surf (Input IN, inout SurfaceOutputStandard o) {
  
      fixed4 col = tex2D (_MainTex, IN.uv_MainTex);
      

      half3 accumulatedLightmap = half3(0, 0, 0);

      // Sample the lightmap using uv2
      // #ifdef LIGHTMAP_ON
      for (int idx = 0; idx < _LightmapCount; idx++) {
        half3 lm = DecodeLightmap(UNITY_SAMPLE_TEX2DARRAY(_LightMaps, float3(IN.uv3_wat, idx)));
        lm *= _LightColors[idx];
        
        accumulatedLightmap += lm;
      }
      // #endif
      o.Albedo = col.rgb;

      o.Alpha = col.a;
    }
    ENDCG
  }
  FallBack "Diffuse"
}