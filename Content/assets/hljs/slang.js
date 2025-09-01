function hljsDefineSlang(hljs) {
    return {
        name: 'Slang',
        keywords: {
            keyword:
                // flow control
                'break continue discard do else for if return while switch case default ' +
                // declarators
                'cbuffer struct class interface namespace typedef var ' +
                // modifiers
                'public private internal ' +
                //properties
                'property get set ' +
                //modules
                'module import ' +
                //etc
                'asm asm_fragment centroid row_major column_major const precise export extern groupshared shared static uniform volatile inline in inout out register unsigned ' +
                'point line lineadj triangle triangleadj packoffset ' +
                'centroid linear nointerpolation noperspective sample ',
            type:
                'void ' +

                'bool ' +

                'half ' +
                'half1 half2 half3 half4 ' +
                'half1x1 half1x2 half1x3 half1x4 ' +
                'half2x1 half2x2 half2x3 half2x4 ' +
                'half3x1 half3x2 half3x3 half3x4 ' +
                'half4x1 half4x2 half4x3 half4x4 ' +
                'float snorm unorm ' +
                'float1 float2 float3 float4 ' +
                'float1x1 float1x2 float1x3 float1x4 ' +
                'float2x1 float2x2 float2x3 float2x4 ' +
                'float3x1 float3x2 float3x3 float3x4 ' +
                'float4x1 float4x2 float4x3 float4x4 ' +
                'double ' +
                'double1 double2 double3 double4 ' +
                'double1x1 double1x2 double1x3 double1x4 ' +
                'double2x1 double2x2 double2x3 double2x4 ' +
                'double3x1 double3x2 double3x3 double3x4 ' +
                'double4x1 double4x2 double4x3 double4x4 ' +
                'int ' +
                'int1 int2 int3 int4 ' +
                'int1x1 int1x2 int1x3 int1x4 ' +
                'int2x1 int2x2 int2x3 int2x4 ' +
                'int3x1 int3x2 int3x3 int3x4 ' +
                'int4x1 int4x2 int4x3 int4x4 ' +
                'uint ' +
                'uint1 uint2 uint3 uint4 ' +
                'uint1x1 uint1x2 uint1x3 uint1x4 ' +
                'uint2x1 uint2x2 uint2x3 uint2x4 ' +
                'uint3x1 uint3x2 uint3x3 uint3x4 ' +
                'uint4x1 uint4x2 uint4x3 uint4x4 ' +
                'dword ' +
                'dword1 dword2 dword3 dword4 ' +
                'dword1x1 dword1x2 dword1x3 dword1x4 ' +
                'dword2x1 dword2x2 dword2x3 dword2x4 ' +
                'dword3x1 dword3x2 dword3x3 dword3x4 ' +
                'dword4x1 dword4x2 dword4x3 dword4x4 ' +

                'min16float ' +
                'min16float1 min16float2 min16float3 min16float4 ' +
                'min16float1x1 min16float1x2 min16float1x3 min16float1x4 ' +
                'min16float2x1 min16float2x2 min16float2x3 min16float2x4 ' +
                'min16float3x1 min16float3x2 min16float3x3 min16float3x4 ' +
                'min16float4x1 min16float4x2 min16float4x3 min16float4x4 ' +
                'min10float ' +
                'min10float1 min10float2 min10float3 min10float4 ' +
                'min10float1x1 min10float1x2 min10float1x3 min10float1x4 ' +
                'min10float2x1 min10float2x2 min10float2x3 min10float2x4 ' +
                'min10float3x1 min10float3x2 min10float3x3 min10float3x4 ' +
                'min10float4x1 min10float4x2 min10float4x3 min10float4x4 ' +
                'min16int ' +
                'min16int1 min16int2 min16int3 min16int4 ' +
                'min16int1x1 min16int1x2 min16int1x3 min16int1x4 ' +
                'min16int2x1 min16int2x2 min16int2x3 min16int2x4 ' +
                'min16int3x1 min16int3x2 min16int3x3 min16int3x4 ' +
                'min16int4x1 min16int4x2 min16int4x3 min16int4x4 ' +
                'min12int ' +
                'min12int1 min12int2 min12int3 min12int4 ' +
                'min12int1x1 min12int1x2 min12int1x3 min12int1x4 ' +
                'min12int2x1 min12int2x2 min12int2x3 min12int2x4 ' +
                'min12int3x1 min12int3x2 min12int3x3 min12int3x4 ' +
                'min12int4x1 min12int4x2 min12int4x3 min12int4x4 ' +
                'min16uint ' +
                'min16uint1 min16uint2 min16uint3 min16uint4 ' +
                'min16uint1x1 min16uint1x2 min16uint1x3 min16uint1x4 ' +
                'min16uint2x1 min16uint2x2 min16uint2x3 min16uint2x4 ' +
                'min16uint3x1 min16uint3x2 min16uint3x3 min16uint3x4 ' +
                'min16uint4x1 min16uint4x2 min16uint4x3 min16uint4x4 ' +

                'vector matrix ' +

                'string ',
            built_in:
                // SV_*
                'SV_Barycentrics SV_ClipDistance SV_CullDistance SV_Coverage SV_CullPrimitive SV_Depth SV_DepthGreaterEqual SV_DepthLessEqual SV_DispatchThreadID SV_DomainLocation SV_DrawIndex SV_DeviceIndex SV_FragInvocationCount SV_FragSize SV_GSInstanceID SV_GroupID SV_GroupIndex SV_GroupThreadID SV_InnerCoverage SV_InsideTessFactor SV_InstanceID SV_IntersectionAttributes SV_IsFrontFace SV_OutputControlPointID SV_PointSize SV_PointCoord SV_Position SV_PrimitiveID SV_RenderTargetArrayIndex SV_SampleIndex SV_ShadingRate SV_StartVertexLocation SV_StartInstanceLocation SV_StencilRef SV_TessFactor SV_VertexID SV_ViewID SV_ViewportArrayIndex SV_VulkanInstanceID SV_VulkanSamplePosition SV_VulkanVertexID ' +
                'SV_Target SV_Target0 SV_Target1 SV_Target2 SV_Target3 SV_Target4 SV_Target5 SV_Target6 SV_Target7 ' +
                // types
                'InputPatch OutputPatch ' +
                'BlendState ConsumeStructuredBuffer DepthStencilState DepthStencilView RasterizerState RenderTargetView SamplerState SamplerComparisonState ' +
                'Buffer RWBuffer ' +
                'StructuredBuffer RWStructuredBuffer ' +
                'ByteAddressBuffer RWByteAddressBuffer ' +
                'texture sampler ' +
                'Texture1D RWTexture1D Texture1DArray RWTexture1DArray ' +
                'Texture2D RWTexture2D Texture2DArray RWTexture2DArray Texture2DMS Texture2DMSArray ' +
                'Texture3D RWTexture3D ' +
                'TextureCube TextureCubeArray ' +
                'PointStream LineStream TriangleStream ' +
                'stateblock stateblock_state ' +
                // functions
                'abort errorf printf ' +
                'abs ceil floor clip clamp saturate fma fmod frac fwidth mad modf min max normalize rcp rsqrt reflect refract sign smoothstep sqrt step trunc ' +
                'sin asin sinh cos acos cosh sincos tan atan atan2 tanh degrees radians ' +
                'exp exp2 frexp ldexp log log10 log2 pow lit msad4 noise ' +
                'all any countbits firstbithigh firstbitlow reversebits isfinite isinf isnan round ' +
                'cross mul determinant distance dst dot faceforward length lerp transpose ' +
                'ddx ddx_coarse ddx_fine ddy ddy_coars ddy_fine ' +
                'asdouble asfloat asint asuint f16tof32 f32tof16 ' +
                'tex1D tex1Dbias tex1Dgrad tex1Dlod tex1Dproj ' +
                'tex2D tex2Dbias tex2Dgrad tex2Dlod tex2Dproj ' +
                'tex3D tex3Dbias tex3Dgrad tex3Dlod tex3Dproj ' +
                'CalculateLevelOfDetail CalculateLevelOfDetailUnclamped Gather GetDimensions GetSamplePosition Load Sample SampleBias SampleCmp SampleCmpLevelZero SampleGrad SampleLevel ' +
                'D3DCOLORtoUBYTE4 ' +
                'AllMemoryBarrier AllMemoryBarrierWithGroupSync DeviceMemoryBarrier DeviceMemoryBarrierWithGroupSync GroupMemoryBarrier GroupMemoryBarrierWithGroupSync ' +
                'InterlockedAdd InterlockedAnd InterlockedCompareExchange InterlockedCompareStore InterlockedExchange InterlockedMax InterlockedMin InterlockedOr InterlockedXor ' +
                'EvaluateAttributeAtCentroid EvaluateAttributeAtSample EvaluateAttributeSnapped ' +
                'GetRenderTargetSampleCount GetRenderTargetSamplePosition ' +
                'Process2DQuadTessFactorsAvg Process2DQuadTessFactorsMax Process2DQuadTessFactorsMin ProcessIsolineTessFactors ProcessQuadTessFactorsAvg ProcessQuadTessFactorsMax ProcessQuadTessFactorsMin ProcessTriTessFactorsAvg ProcessTriTessFactorsMax ProcessTriTessFactorsMin ' +
                'CheckAccessFullyMapped ' +
                'AppendStructuredBuffer',
            literal: 'true false NULL'
        },
        illegal: '"',
        contains: [
            hljs.C_LINE_COMMENT_MODE,
            hljs.C_BLOCK_COMMENT_MODE,
            hljs.C_NUMBER_MODE,
            {
                className: 'meta',
                begin: '#', end: '$'
            }
        ]
    };
}