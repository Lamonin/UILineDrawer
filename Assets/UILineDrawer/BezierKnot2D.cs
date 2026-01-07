using System;
using Unity.Mathematics;

namespace Maro.UILineDrawer
{
    /// <summary>
    /// Represents a single control point in the 2D spline.
    /// Tangents are relative to the Position.
    /// </summary>
    [Serializable]
    public struct BezierKnot2D
    {
        public float3 Position;
        public float3 TangentIn;
        public float3 TangentOut;
        public quaternion Rotation;

        public BezierKnot2D(float3 position)
        {
            Position = position;
            TangentIn = float3.zero;
            TangentOut = float3.zero;
            Rotation = quaternion.identity;
        }

        public BezierKnot2D(float3 position, float3 tangentIn, float3 tangentOut)
        {
            Position = position;
            TangentIn = tangentIn;
            TangentOut = tangentOut;
            Rotation = quaternion.identity;
        }
    }
}