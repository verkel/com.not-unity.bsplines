using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;

namespace UnityEngine.BSplines
{
    /// <summary>
    /// A collection of methods for extracting information about <see cref="BezierCurve"/> types.
    /// </summary>
    public static class CurveUtility
    {
        struct FrenetFrame
        {
            public float3 origin;
            public float3 tangent;
            public float3 normal;
            public float3 binormal;
        }

        const int k_NormalsPerCurve = 16;
        
        /// <summary>
        /// Given a Bezier curve, return an interpolated position at ratio t.
        /// </summary>
        /// <param name="curve">A cubic Bezier curve.</param>
        /// <param name="t">A value between 0 and 1 representing the ratio along the curve.</param>
        /// <returns>A position on the curve.</returns>
        public static float3 EvaluatePosition(BSplineCurve curve,  float t)
        {
            float t1 = math.clamp(t, 0, 1);
            float t2 = t1 * t1;
            float t3 = t2 * t1;

            var p0 = curve.P0;
            var p1 = curve.P1;
            var p2 = curve.P2;
            var p3 = curve.P3;

            var c0 = p0 + 4f * p1 + p2;
            var c1 = -3f * p0 + 3f * p2;
            var c2 = 3f * p0 - 6f * p1 + 3f * p2;
            var c3 = -p0 + 3f * p1 - 3f * p2 + p3;

            var position = (c0 + c1 * t1 + c2 * t2 + c3 * t3) / 6f;

            return position;
        }

        /// <summary>
        /// Given a Bezier curve, return an interpolated tangent at ratio t.
        /// </summary>
        /// <param name="curve">A cubic Bezier curve.</param>
        /// <param name="t">A value between 0 and 1 representing the ratio along the curve.</param>
        /// <returns>A tangent on the curve.</returns>
        public static float3 EvaluateTangent(BSplineCurve curve, float t)
        {
            float t1 = math.clamp(t, 0, 1);
            float t2 = t1 * t1;

            var p0 = curve.P0;
            var p1 = curve.P1;
            var p2 = curve.P2;
            var p3 = curve.P3;

            var c1 = -3f * p0 + 3f * p2;
            var c2 = 3f * p0 - 6f * p1 + 3f * p2;
            var c3 = -p0 + 3f * p1 - 3f * p2 + p3;

            var tangent = (c1 + c2 * 2f * t1 + c3 * 3f * t2) / 6f;

            return tangent;
        }

        /// <summary>
        /// Given a Bezier curve, return an interpolated acceleration at ratio t.
        /// </summary>
        /// <param name="curve">A cubic Bezier curve.</param>
        /// <param name="t">A value between 0 and 1 representing the ratio along the curve.</param>
        /// <returns>An acceleration vector on the curve.</returns>
        public static float3 EvaluateAcceleration(BSplineCurve curve,  float t)
        {
            float t1 = math.clamp(t, 0, 1);

            var p0 = curve.P0;
            var p1 = curve.P1;
            var p2 = curve.P2;
            var p3 = curve.P3;

            var c2 = 3f * p0 - 6f * p1 + 3f * p2;
            var c3 = -p0 + 3f * p1 - 3f * p2 + p3;

            var acceleration = (c2 * 2f + c3 * 6f * t1) / 6f;

            return acceleration;
        }

        /// <summary>
        /// Given a Bezier curve, return an interpolated curvature at ratio t.
        /// </summary>
        /// <param name="curve">A cubic Bezier curve.</param>
        /// <param name="t">A value between 0 and 1 representing the ratio along the curve.</param>
        /// <returns>A curvature value on the curve.</returns>
        public static float EvaluateCurvature(BSplineCurve curve, float t)
        {
            t = math.clamp(t, 0, 1);

            var firstDerivative = EvaluateTangent(curve, t);
            var secondDerivative = EvaluateAcceleration(curve, t);
            var firstDerivativeNormSq = math.lengthsq(firstDerivative);
            var secondDerivativeNormSq = math.lengthsq(secondDerivative);
            var derivativesDot = math.dot(firstDerivative, secondDerivative);

            var kappa = math.sqrt(
                    ( firstDerivativeNormSq * secondDerivativeNormSq ) - ( derivativesDot * derivativesDot ))
                / ( firstDerivativeNormSq * math.length(firstDerivative));

            return kappa;
        }

        /// <summary>
        /// Calculate the length of a <see cref="BezierCurve"/> by unrolling the curve into linear segments and summing
        /// the lengths of the lines. This is equivalent to accessing <see cref="Spline.GetCurveLength"/>.
        /// </summary>
        /// <param name="curve">The <see cref="BezierCurve"/> to calculate length.</param>
        /// <param name="resolution">The number of linear segments used to calculate the curve length.</param>
        /// <returns>The sum length of a collection of linear segments fitting this curve.</returns>
        /// <seealso cref="ApproximateLength(BezierCurve)"/>
        public static float CalculateLength(BSplineCurve curve, int resolution = 30)
        {
            float magnitude = 0f;
            float3 prev = EvaluatePosition(curve, 0f);

            for (int i = 1; i < resolution; i++)
            {
                var point = EvaluatePosition(curve, i / (resolution - 1f));
                var dir = point - prev;
                magnitude += math.length(dir);
                prev = point;
            }

            return magnitude;
        }

        /// <summary>
        /// Populate a pre-allocated lookupTable array with distance to 't' values. The number of table entries is
        /// dependent on the size of the passed lookupTable.
        /// </summary>
        /// <param name="curve">The <see cref="BezierCurve"/> to create a distance to 't' lookup table for.</param>
        /// <param name="lookupTable">A pre-allocated array to populate with distance to interpolation ratio data.</param>
        public static void CalculateCurveLengths(BSplineCurve curve, DistanceToInterpolation[] lookupTable)
        {
            var resolution = lookupTable.Length;

            float magnitude = 0f;
            float3 prev = EvaluatePosition(curve, 0f);
            lookupTable[0] = new DistanceToInterpolation() { Distance = 0f , T = 0f };

            for (int i = 1; i < resolution; i++)
            {
                var t = i / ( resolution - 1f );
                var point = EvaluatePosition(curve, t);
                var dir = point - prev;
                magnitude += math.length(dir);
                lookupTable[i] = new DistanceToInterpolation() { Distance = magnitude , T = t};
                prev = point;
            }
        }
        
        const float k_Epsilon = 0.0001f;
        /// <summary>
        /// Mathf.Approximately is not working when using BurstCompile, causing NaN values in the EvaluateUpVector
        /// method when tangents have a 0 length. Using this method instead fixes that.
        /// </summary>
        static bool Approximately(float a, float b)
        {
            // Reusing Mathf.Approximately code
            return math.abs(b - a) < math.max(0.000001f * math.max(math.abs(a), math.abs(b)), k_Epsilon * 8);
        }

        static readonly DistanceToInterpolation[] k_DistanceLUT = new DistanceToInterpolation[24];

        /// <summary>
        /// Gets the normalized interpolation, (t), that corresponds to a distance on a <see cref="BezierCurve"/>.
        /// </summary>
        /// <remarks>
        /// It is inefficient to call this method frequently. For better performance create a
        /// <see cref="DistanceToInterpolation"/> cache with <see cref="CalculateCurveLengths"/> and use the
        /// overload of this method which accepts a lookup table.
        /// </remarks>
        /// <param name="curve">The <see cref="BezierCurve"/> to calculate the distance to interpolation ratio for.</param>
        /// <param name="distance">The curve-relative distance to convert to an interpolation ratio (also referred to as 't').</param>
        /// <returns> Returns the normalized interpolation ratio associated to distance on the designated curve.</returns>
        public static float GetDistanceToInterpolation(BSplineCurve curve, float distance)
        {
            CalculateCurveLengths(curve, k_DistanceLUT);
            return GetDistanceToInterpolation(k_DistanceLUT, distance);
        }

        /// <summary>
        /// Return the normalized interpolation (t) corresponding to a distance on a <see cref="BezierCurve"/>. This
        /// method accepts a look-up table (referred to in code with acronym "LUT") that may be constructed using
        /// <see cref="CalculateCurveLengths"/>. The built-in Spline class implementations (<see cref="Spline"/> and
        /// <see cref="NativeSpline"/>) cache these look-up tables internally.
        /// </summary>
        /// <typeparam name="T">The collection type.</typeparam>
        /// <param name="lut">A look-up table of distance to 't' values. See <see cref="CalculateCurveLengths"/> for creating
        /// this collection.</param>
        /// <param name="distance">The curve-relative distance to convert to an interpolation ratio (also referred to as 't').</param>
        /// <returns>  The normalized interpolation ratio associated to distance on the designated curve.</returns>
        public static float GetDistanceToInterpolation<T>(T lut, float distance) where T : IReadOnlyList<DistanceToInterpolation>
        {
            if(lut == null || lut.Count < 1 || distance <= 0)
                return 0f;

            var resolution = lut.Count;
            var curveLength = lut[resolution-1].Distance;

            if(distance >= curveLength)
                return 1f;

            var prev = lut[0];

            for(int i = 1; i < resolution; i++)
            {
                var current = lut[i];
                if(distance < current.Distance)
                    return math.lerp(prev.T, current.T, (distance - prev.Distance) / (current.Distance - prev.Distance));
                prev = current;
            }

            return 1f;
        }

        /// <summary>
        /// Gets the point on a <see cref="BezierCurve"/> nearest to a ray.
        /// </summary>
        /// <param name="curve">The <see cref="BezierCurve"/> to compare.</param>
        /// <param name="ray">The input ray.</param>
        /// <param name="resolution">The number of line segments on this curve that are rasterized when testing
        /// for the nearest point. A higher value is more accurate, but slower to calculate.</param>
        /// <returns>Returns the nearest position on the curve to a ray.</returns>
        public static float3 GetNearestPoint(BSplineCurve curve, Ray ray, int resolution = 16)
        {
            GetNearestPoint(curve, ray, out var position, out _, resolution);
            return position;
        }

        /// <summary>
        /// Gets the point on a <see cref="BezierCurve"/> nearest to a ray.
        /// </summary>
        /// <param name="curve">The <see cref="BezierCurve"/> to compare.</param>
        /// <param name="ray">The input ray.</param>
        /// <param name="position">The nearest position on the curve to a ray.</param>
        /// <param name="interpolation">The ratio from range 0 to 1 along the curve at which the nearest point is located.</param>
        /// <param name="resolution">The number of line segments that this curve will be rasterized to when testing
        /// for nearest point. A higher value will be more accurate, but slower to calculate.</param>
        /// <returns>The distance from ray to nearest point on a <see cref="BezierCurve"/>.</returns>
        public static float GetNearestPoint(BSplineCurve curve, Ray ray, out float3 position, out float interpolation, int resolution = 16)
        {
            float bestDistSqr = float.PositiveInfinity;
            float bestLineParam = 0f;

            interpolation = 0f;
            position = float3.zero;

            float3 a = EvaluatePosition(curve, 0f);
            float3 ro = ray.origin, rd = ray.direction;

            for (int i = 1; i < resolution; ++i)
            {
                float t = i / (resolution - 1f);
                float3 b = EvaluatePosition(curve, t);

                var (rayPoint, linePoint) = SplineMath.RayLineNearestPoint(ro, rd, a, b, out _, out var lineParam);
                var distSqr = math.lengthsq(linePoint - rayPoint);

                if (distSqr < bestDistSqr)
                {
                    position = linePoint;
                    bestDistSqr = distSqr;
                    bestLineParam = lineParam;
                    interpolation = t;
                }

                a = b;
            }

            interpolation += bestLineParam * (1f / (resolution - 1f));
            return math.sqrt(bestDistSqr);
        }
    }
}
