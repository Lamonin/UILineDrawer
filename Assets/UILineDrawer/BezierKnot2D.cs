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
        public float2 Position;
        public float2 TangentIn;
        public float2 TangentOut;
        public float Rotation;

        public BezierKnot2D(float2 position)
        {
            Position = position;
            TangentIn = float2.zero;
            TangentOut = float2.zero;
            Rotation = 0;
        }

        public BezierKnot2D(float2 position, float2 tangentIn, float2 tangentOut, float rotation = 0)
        {
            Position = position;
            TangentIn = tangentIn;
            TangentOut = tangentOut;
            Rotation = rotation;
        }
    }
}