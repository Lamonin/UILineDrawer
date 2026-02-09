using UnityEngine;

namespace Maro.UILineDrawer
{
    internal static class RaycastOffsetUtility
    {
        public static void Sanitize(
            ref float raycastExtraThickness,
            RaycastOffsetMode offsetMode,
            ref float startOffset,
            ref float endOffset
        )
        {
            raycastExtraThickness = Mathf.Max(raycastExtraThickness, 0f);

            if (offsetMode == RaycastOffsetMode.Normalized)
            {
                startOffset = Mathf.Clamp01(startOffset);
                endOffset = Mathf.Clamp01(endOffset);
            }
            else
            {
                startOffset = Mathf.Max(startOffset, 0f);
                endOffset = Mathf.Max(endOffset, 0f);
            }
        }

        public static void Convert(
            RaycastOffsetMode fromMode,
            RaycastOffsetMode toMode,
            float length,
            ref float startOffset,
            ref float endOffset
        )
        {
            if (fromMode == toMode || length <= 0f)
            {
                return;
            }

            if (fromMode == RaycastOffsetMode.Normalized
                && toMode == RaycastOffsetMode.Fixed)
            {
                startOffset *= length;
                endOffset *= length;
                return;
            }

            if (fromMode == RaycastOffsetMode.Fixed
                && toMode == RaycastOffsetMode.Normalized)
            {
                float invLength = 1f / length;
                startOffset *= invLength;
                endOffset *= invLength;
            }
        }

        public static bool IsPointInsideRange(
            RaycastOffsetMode offsetMode,
            float startOffset,
            float endOffset,
            float normalizedT,
            float distanceAlongLine,
            float totalLength
        )
        {
            if (offsetMode == RaycastOffsetMode.Normalized)
            {
                if (startOffset + endOffset >= 1f)
                {
                    return false;
                }

                return normalizedT >= startOffset && normalizedT <= (1f - endOffset);
            }

            if (totalLength <= 0f)
            {
                return false;
            }

            float clampedStartOffset = Mathf.Clamp(startOffset, 0f, totalLength);
            float clampedEndOffset = Mathf.Clamp(endOffset, 0f, totalLength);
            if (clampedStartOffset + clampedEndOffset >= totalLength)
            {
                return false;
            }

            return distanceAlongLine >= clampedStartOffset
                   && distanceAlongLine <= (totalLength - clampedEndOffset);
        }
    }
}