using UnityEngine;

namespace Maro.UILineDrawer
{
    [System.Serializable]
    public struct Spline2DPoint
    {
        public Vector3 Position;
        public float Rotation;
        public Vector3 TangentIn;
        public Vector3 TangentOut;
    }
}