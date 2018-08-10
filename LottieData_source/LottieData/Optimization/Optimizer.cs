// Copyright(c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;

namespace LottieData.Optimization
{
    /// <summary>
    /// Creates and caches optimized versions of Lottie data. The optimized data is functionally
    /// equivalent to unoptimized data, but may be represented more efficiently.
    /// </summary>
#if !WINDOWS_UWP
    public
#endif
    sealed class Optimizer
    {
        static readonly AnimatableComparer<Color> s_colorComparer = new AnimatableComparer<Color>();
        static readonly AnimatableComparer<double> s_floatComparer = new AnimatableComparer<double>();
        static readonly AnimatableComparer<PathGeometry> s_pathGeometryComparer = new AnimatableComparer<PathGeometry>();
        readonly Dictionary<Animatable<Color>, Animatable<Color>> _animatableColorsCache;
        readonly Dictionary<Animatable<double>, Animatable<double>> _animatableFloatsCache;
        readonly Dictionary<Animatable<PathGeometry>, Animatable<PathGeometry>> _animatablePathGeometriesCache;

        public Optimizer()
        {
            _animatableColorsCache = new Dictionary<Animatable<Color>, Animatable<Color>>(s_colorComparer);
            _animatableFloatsCache = new Dictionary<Animatable<double>, Animatable<double>>(s_floatComparer);
            _animatablePathGeometriesCache = new Dictionary<Animatable<PathGeometry>, Animatable<PathGeometry>>(s_pathGeometryComparer);
        }

        public Animatable<Color> GetOptimized(Animatable<Color> value) => GetOptimized(value, s_colorComparer, _animatableColorsCache);
        public Animatable<double> GetOptimized(Animatable<double> value) => GetOptimized(value, s_floatComparer, _animatableFloatsCache);
        public Animatable<PathGeometry> GetOptimized(Animatable<PathGeometry> value)
        {
            var optimized = GetOptimized(value, s_pathGeometryComparer, _animatablePathGeometriesCache);
            // If the geometries have different numbers of segments they can't be animated. However
            // in one specific case we can fix that.
            var geometries = value.KeyFrames.Select(kf => kf.Value.Beziers.ToArray()).ToArray();
            var distinctSegmentCounts = geometries.Select(g => g.Length).Distinct().Count();

            if (distinctSegmentCounts != 2)
            {
                return optimized;
            }

            // The geometries have different numbers of segments. See if this is the fixable case.
            // Requires:
            //  * Every segment is a line.
            //  * Geometries have only 1 or 2 segments.
            //  * If there are 2 segments, the second segment draws back over the first.
            foreach (var g in geometries)
            {
                foreach (var segment in g)
                {
                    if (!segment.IsALine)
                    {
                        return optimized;
                    }
                }
                switch(g.Length)
                {
                    default:
                        return optimized;
                    case 1:
                        if (!g[0].IsALine)
                        {
                            return optimized;
                        }
                        break;
                    case 2:
                        if (!g[0].IsALine || !g[1].IsALine)
                        {
                            return optimized;
                        }

                        // Start of line 0
                        var a = g[0].ControlPoint0;
                        // End of line 0
                        var b = g[0].ControlPoint3;
                        // End of line 1
                        var c = g[1].ControlPoint3;

                        if (!BezierSegment.ArePointsColinear(0, a, b, c))
                        {
                            return optimized;
                        }

                        if (!IsBetween(a, c, b))
                        {
                            return optimized;
                        }
                        // We can handle this case - the second segment draws back over the first.
                        break;
                }
            }

            // Create a new Animatable<PathGeometry> which has only one segment in each keyframe.
            var hacked = optimized.KeyFrames.Select(pg => HackPathGeometry(pg));
            return new Animatable<PathGeometry>(hacked.First().Value, hacked, optimized.PropertyIndex);
        }

        static KeyFrame<PathGeometry> HackPathGeometry(KeyFrame<PathGeometry> value)
        {
            return new KeyFrame<PathGeometry>(value.Frame, new PathGeometry(new[] { value.Value.Beziers.First() }), Vector3.Zero, Vector3.Zero, value.Easing);
        }

        // True iff b is between and c.
        static bool IsBetween(Vector2 a, Vector2 b, Vector2 c)
        {
            return
                IsBetween(a.X, b.X, c.X) &&
                IsBetween(a.Y, b.Y, c.Y);
        }


        // True iff b is between a and c.
        static bool IsBetween(double a, double b, double c)
        {
            var deltaAC = Math.Abs(a - c);

            if (Math.Abs(a - b) > deltaAC)
            {
                return false;
            }
            if (Math.Abs(c - b) > deltaAC)
            {
                return false;
            }
            return true;
        }
        static Animatable<T> GetOptimized<T>(Animatable<T> value, AnimatableComparer<T> comparer, Dictionary<Animatable<T>, Animatable<T>> cache) where T : IEquatable<T>
        {
            if (!cache.TryGetValue(value, out Animatable<T> result))
            {
                // Nothing in the cache yet.
                if (!value.IsAnimated)
                {
                    // The value isn't animated, so the keyframe optimization doesn't apply.
                    result = value;
                }
                else
                {
                    var keyFrames = OptimizeKeyFrames(value.InitialValue, value.KeyFrames).ToArray();
                    if (comparer.Equals(keyFrames, value.KeyFrames))
                    {
                        // Optimization didn't achieve anything.
                        result = value;
                    }
                    else
                    {
                        var optimized = new Animatable<T>(value.InitialValue, keyFrames, null);
                        result = optimized;
                    }
                }
                cache.Add(value, result);
            }
            return result;
        }

        // Returns a list of KeyFrames with any redundant frames removed.
        static IEnumerable<KeyFrame<T>> OptimizeKeyFrames<T>(T initialValue, IEnumerable<KeyFrame<T>> keyFrames) where T : IEquatable<T>
        {
            T previousValue = initialValue;
            KeyFrame<T> currentKeyFrame = keyFrames.First();
            bool atLeastOneWasOutput = false;
            foreach (var nextKeyFrame in keyFrames.Skip(1))
            {
                if (!currentKeyFrame.Value.Equals(previousValue))
                {
                    // The KeyFrame changes the value, so it must be output.
                    yield return currentKeyFrame;
                    atLeastOneWasOutput = true;
                }
                else if (!currentKeyFrame.Value.Equals(nextKeyFrame.Value))
                {
                    // The current frame has the same value as previous, but it starts a ramp to 
                    // the next value, so it must be output.
                    // Seeing as the value isn't changing, the easing doesn't matter. Linear is the 
                    // simplest so always use Linear when the value isn't changing.
                    if (currentKeyFrame.Easing.Type != Easing.EasingType.Linear)
                    {
                        currentKeyFrame = new KeyFrame<T>(
                            currentKeyFrame.Frame, 
                            currentKeyFrame.Value, 
                            currentKeyFrame.SpatialControlPoint1, 
                            currentKeyFrame.SpatialControlPoint2, 
                            LinearEasing.Instance);
                    }
                    yield return currentKeyFrame;
                    atLeastOneWasOutput = true;
                }
                previousValue = currentKeyFrame.Value;
                currentKeyFrame = nextKeyFrame;
            }

            // Final frame. Only necessary if at least one keyframe was output and current KeyFrame changes the value.
            if (atLeastOneWasOutput && !currentKeyFrame.Value.Equals(previousValue))
            {
                // The final frame is necessary
                yield return currentKeyFrame;
            }
        }

        /// <summary>
        /// Returns at most 1 key frame with progress less than or equal <paramref name="startFrame"/>, and
        /// at most 1 key frame with progress greater than or equal <paramref name="endFrame"/>.
        /// </summary>
        public IEnumerable<KeyFrame<T>> GetTrimmed<T>(
            IEnumerable<KeyFrame<T>> keyFrames, 
            double startFrame, 
            double endFrame) where T : IEquatable<T>
        {
            bool firstKeyFrameReturned = false;
            KeyFrame<T> firstCandidate = null;
            foreach (var keyFrame in keyFrames)
            {
                if (keyFrame.Frame <= startFrame)
                {
                    firstCandidate = keyFrame;
                }
                else if (keyFrame.Frame == 0)
                {
                    firstCandidate = null;
                    yield return keyFrame;
                    firstKeyFrameReturned = true;
                }
                else
                {
                    if (!firstKeyFrameReturned && firstCandidate != null)
                    {
                        yield return firstCandidate;
                        firstKeyFrameReturned = true;
                    }
                    yield return keyFrame;
                    if (keyFrame.Frame >= endFrame)
                    {
                        yield break;
                    }
                }
            }

        }

        sealed class AnimatableComparer<T>
            : IEqualityComparer<IEnumerable<KeyFrame<T>>>
            , IEqualityComparer<KeyFrame<T>>
            , IEqualityComparer<Easing>
            , IEqualityComparer<Animatable<T>>
            where T : IEquatable<T>
        {
            public bool Equals(KeyFrame<T> x, KeyFrame<T> y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (x == null || y == null)
                {
                    return false;
                }
                return x.Equals(y);
            }

            public bool Equals(IEnumerable<KeyFrame<T>> x, IEnumerable<KeyFrame<T>> y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (x == null || y == null)
                {
                    return false;
                }
                return x.SequenceEqual(y);
            }

            public bool Equals(Easing x, Easing y) => Equates(x, y);

            public bool Equals(Animatable<T> x, Animatable<T> y)
            {
                if (ReferenceEquals(x, y))
                {
                    return true;
                }
                if (x == null || y == null)
                {
                    return false;
                }
                return x.InitialValue.Equals(y.InitialValue) && Equals(x.KeyFrames, y.KeyFrames);
            }

            public int GetHashCode(KeyFrame<T> obj) => obj.GetHashCode();

            public int GetHashCode(IEnumerable<KeyFrame<T>> obj) => obj.Select(kf => kf.GetHashCode()).Aggregate((a, b) => a ^ b);

            public int GetHashCode(Easing obj) => obj.GetHashCode();

            public int GetHashCode(Animatable<T> obj) => obj.GetHashCode();

            // Compares 2 IEquatable<V> for equality.
            static bool Equates<V>(V x, V y) where V : class, IEquatable<V> => x == null ? y == null : x.Equals(y);
        }
    }
}
