Shader "Custom/GrassShader"
{
    Properties
    {
        _BaseColor     ("Base Color",     Color)  = (0.15, 0.55, 0.10, 1)
        _TipColor      ("Tip Color",      Color)  = (0.40, 0.80, 0.20, 1)
        _WindStrength  ("Wind Strength",  Float)  = 0.30
        _WindSpeed     ("Wind Speed",     Float)  = 1.50
        _WindFrequency ("Wind Frequency", Float)  = 1.00
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // Render both sides — no need to double the mesh geometry
        Cull Off

        CGPROGRAM
        // Surface shader, Lambert lighting, custom vertex function, cast shadows
        #pragma surface surf Lambert vertex:vert addshadow
        // Required for Graphics.DrawMeshInstanced
        #pragma multi_compile_instancing

        // ---- per-instance properties (empty here, but macro pair is required) ----
        UNITY_INSTANCING_BUFFER_START(Props)
        UNITY_INSTANCING_BUFFER_END(Props)

        // ---- uniforms ----
        fixed4 _BaseColor;
        fixed4 _TipColor;
        float  _WindStrength;
        float  _WindSpeed;
        float  _WindFrequency;

        // ---- custom Input: carry the blade height (UV.y) from vert to surf ----
        struct Input
        {
            float bladeV; // 0 = root, 1 = tip
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            UNITY_SETUP_INSTANCE_ID(v);

            // UV.y encodes height along the blade (set in GrassSpawner.CreateBladeMesh)
            float t    = v.texcoord.y;
            o.bladeV   = t;

            float time = _Time.y * _WindSpeed;

            // Roots are pinned; tips sway fully
            v.vertex.x += sin(_WindFrequency * v.vertex.x + time)           * _WindStrength * t;
            v.vertex.z += cos(_WindFrequency * v.vertex.z + time * 0.8)     * _WindStrength * 0.5 * t;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            // Gradient from dark green at root to bright green at tip
            o.Albedo = lerp(_BaseColor, _TipColor, IN.bladeV).rgb;
        }
        ENDCG
    }

    FallBack "Diffuse"
}
