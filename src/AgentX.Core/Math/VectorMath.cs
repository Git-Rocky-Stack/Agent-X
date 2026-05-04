using System.Runtime.CompilerServices;

namespace AgentX.Core.Mathematics;

/// <summary>
/// Utility class for vector mathematics operations used throughout the AI component.
/// Provides high-performance, SIMD-friendly implementations of common operations.
/// All methods are static and thread-safe.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes the cosine similarity between two vectors.
    /// Returns 1.0 for identical vectors, 0.0 for orthogonal vectors, -1.0 for opposite vectors.
    /// Throws if vectors have different dimensions.
    /// </summary>
    /// <param name="a">First vector (read-only span for zero-allocation).</param>
    /// <param name="b">Second vector (read-only span for zero-allocation).</param>
    /// <returns>Cosine similarity in range [-1.0, 1.0]. Returns 0.0 if either vector has zero magnitude.</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"Vector dimension mismatch: left has {a.Length} dimensions, right has {b.Length} dimensions.",
                nameof(b));
        }

        if (a.Length == 0)
            return 0f;

        double dot = 0;
        double magA = 0;
        double magB = 0;

        // Unrolled loop for better performance (process 4 elements at a time)
        int i = 0;
        int simdLimit = a.Length & ~3; // Round down to nearest multiple of 4

        for (; i < simdLimit; i += 4)
        {
            dot += a[i] * b[i] + a[i + 1] * b[i + 1] + a[i + 2] * b[i + 2] + a[i + 3] * b[i + 3];
            magA += a[i] * a[i] + a[i + 1] * a[i + 1] + a[i + 2] * a[i + 2] + a[i + 3] * a[i + 3];
            magB += b[i] * b[i] + b[i + 1] * b[i + 1] + b[i + 2] * b[i + 2] + b[i + 3] * b[i + 3];
        }

        // Process remaining elements
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        double denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
        return denominator > 0f ? (float)(dot / denominator) : 0f;
    }

    /// <summary>
    /// Computes cosine similarity between two float arrays.
    /// Overload for convenience when working with arrays rather than spans.
    /// </summary>
    /// <param name="a">First vector as array.</param>
    /// <param name="b">Second vector as array.</param>
    /// <returns>Cosine similarity in range [-1.0, 1.0]. Returns 0.0 if either vector has zero magnitude.</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths or are null.</exception>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a is null)
            throw new ArgumentNullException(nameof(a));
        if (b is null)
            throw new ArgumentNullException(nameof(b));

        return CosineSimilarity(new ReadOnlySpan<float>(a), new ReadOnlySpan<float>(b));
    }

    /// <summary>
    /// Computes the cosine similarity between two vectors where magnitudes are pre-computed.
    /// More efficient when computing multiple similarities against the same query vector.
    /// </summary>
    /// <param name="dot">Pre-computed dot product of the two vectors.</param>
    /// <param name="magnitudeA">Pre-computed magnitude (L2 norm) of vector A.</param>
    /// <param name="magnitudeB">Pre-computed magnitude (L2 norm) of vector B.</param>
    /// <returns>Cosine similarity in range [-1.0, 1.0]. Returns 0.0 if either magnitude is zero.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static float CosineSimilarityFromMagnitudes(double dot, double magnitudeA, double magnitudeB)
    {
        if (magnitudeA <= 0 || magnitudeB <= 0)
            return 0f;

        double denominator = magnitudeA * magnitudeB;
        return (float)(dot / denominator);
    }

    /// <summary>
    /// Computes the L2 (Euclidean) magnitude of a vector.
    /// </summary>
    /// <param name="vector">The input vector.</param>
    /// <returns>The L2 norm (magnitude) of the vector.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static double Magnitude(ReadOnlySpan<float> vector)
    {
        if (vector.Length == 0)
            return 0.0;

        double sum = 0;
        int i = 0;
        int simdLimit = vector.Length & ~3;

        for (; i < simdLimit; i += 4)
        {
            sum += vector[i] * vector[i] + vector[i + 1] * vector[i + 1] +
                   vector[i + 2] * vector[i + 2] + vector[i + 3] * vector[i + 3];
        }

        for (; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Computes the dot product of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The dot product (sum of element-wise products).</returns>
    /// <exception cref="ArgumentException">Thrown when vectors have different lengths.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static double DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
        {
            throw new ArgumentException(
                $"Vector dimension mismatch: left has {a.Length} dimensions, right has {b.Length} dimensions.",
                nameof(b));
        }

        if (a.Length == 0)
            return 0.0;

        double sum = 0;
        int i = 0;
        int simdLimit = a.Length & ~3;

        for (; i < simdLimit; i += 4)
        {
            sum += a[i] * b[i] + a[i + 1] * b[i + 1] + a[i + 2] * b[i + 2] + a[i + 3] * b[i + 3];
        }

        for (; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    /// <summary>
    /// Computes the Euclidean (L2) distance between two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The L2 distance. Returns double.MaxValue if vectors have different dimensions.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static double EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return double.MaxValue;

        if (a.Length == 0)
            return 0.0;

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double diff = a[i] - b[i];
            sum += diff * diff;
        }

        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Normalizes a vector to unit length (L2 normalization).
    /// </summary>
    /// <param name="vector">The vector to normalize (modified in-place).</param>
    /// <returns>The original magnitude before normalization. Returns 0 if vector is all zeros.</returns>
    public static double Normalize(Span<float> vector)
    {
        double mag = Magnitude(vector);
        if (mag > 0)
        {
            double scale = 1.0 / mag;
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] = (float)(vector[i] * scale);
            }
        }
        return mag;
    }

    /// <summary>
    /// Clamps a value to the range [0, 1].
    /// Useful for ensuring similarity scores are valid probabilities.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

    /// <summary>
    /// Clamps a value to the range [0, 1].
    /// Useful for ensuring similarity scores are valid probabilities.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp01(double value) => Math.Max(0.0, Math.Min(1.0, value));

    /// <summary>
    /// Clamps a value between a minimum and maximum value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

    /// <summary>
    /// Clamps a value between a minimum and maximum value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));

    /// <summary>
    /// Returns the larger of two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Max(float a, float b) => Math.Max(a, b);

    /// <summary>
    /// Returns the larger of two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Max(double a, double b) => Math.Max(a, b);

    /// <summary>
    /// Returns the smaller of two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Min(float a, float b) => Math.Min(a, b);

    /// <summary>
    /// Returns the smaller of two values.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Min(double a, double b) => Math.Min(a, b);

    /// <summary>
    /// Returns the square root of a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Sqrt(float value) => (float)Math.Sqrt(value);

    /// <summary>
    /// Returns the square root of a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sqrt(double value) => Math.Sqrt(value);

    /// <summary>
    /// Rounds a value to the nearest integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Round(float value) => (float)Math.Round(value);

    /// <summary>
    /// Rounds a value to the nearest integer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Round(double value) => Math.Round(value);

    /// <summary>
    /// Computes the Manhattan (L1) distance between two vectors.
    /// Useful for certain similarity metrics and as a cheaper alternative to L2.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static double ManhattanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            return double.MaxValue;

        if (a.Length == 0)
            return 0.0;

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return sum;
    }
}
