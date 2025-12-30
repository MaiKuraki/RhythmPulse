/*
 * EasingCore (https://github.com/setchi/EasingCore)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/EasingCore/blob/master/LICENSE)
 */

using UnityEngine;
using System.Runtime.CompilerServices;

namespace EasingCore
{
    public enum Ease
    {
        Linear,
        InBack,
        InBounce,
        InCirc,
        InCubic,
        InElastic,
        InExpo,
        InQuad,
        InQuart,
        InQuint,
        InSine,
        OutBack,
        OutBounce,
        OutCirc,
        OutCubic,
        OutElastic,
        OutExpo,
        OutQuad,
        OutQuart,
        OutQuint,
        OutSine,
        InOutBack,
        InOutBounce,
        InOutCirc,
        InOutCubic,
        InOutElastic,
        InOutExpo,
        InOutQuad,
        InOutQuart,
        InOutQuint,
        InOutSine,
    }

    public delegate float EasingFunction(float t);

    public static class Easing
    {
        private static readonly EasingFunction[] Functions =
        {
            Linear,
            InBack,
            InBounce,
            InCirc,
            InCubic,
            InElastic,
            InExpo,
            InQuad,
            InQuart,
            InQuint,
            InSine,
            OutBack,
            OutBounce,
            OutCirc,
            OutCubic,
            OutElastic,
            OutExpo,
            OutQuad,
            OutQuart,
            OutQuint,
            OutSine,
            InOutBack,
            InOutBounce,
            InOutCirc,
            InOutCubic,
            InOutElastic,
            InOutExpo,
            InOutQuad,
            InOutQuart,
            InOutQuint,
            InOutSine,
        };

        /// <summary>
        /// Gets the easing function by type.
        /// Optimized to use a static array lookup for O(1) access and zero GC allocations.
        /// </summary>
        /// <param name="type">The ease type.</param>
        /// <returns>The corresponding easing function.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static EasingFunction Get(Ease type) => Functions[(int)type];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Linear(float t) => t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InBack(float t) => t * t * t - t * Mathf.Sin(t * Mathf.PI);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutBack(float t) => 1f - InBack(1f - t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutBack(float t) =>
            t < 0.5f
                ? 0.5f * InBack(2f * t)
                : 0.5f * OutBack(2f * t - 1f) + 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InBounce(float t) => 1f - OutBounce(1f - t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutBounce(float t) =>
            t < 4f / 11.0f ? (121f * t * t) / 16.0f :
            t < 8f / 11.0f ? (363f / 40.0f * t * t) - (99f / 10.0f * t) + 17f / 5.0f :
            t < 9f / 10.0f ? (4356f / 361.0f * t * t) - (35442f / 1805.0f * t) + 16061f / 1805.0f :
            (54f / 5.0f * t * t) - (513f / 25.0f * t) + 268f / 25.0f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutBounce(float t) =>
            t < 0.5f
                ? 0.5f * InBounce(2f * t)
                : 0.5f * OutBounce(2f * t - 1f) + 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InCirc(float t) => 1f - Mathf.Sqrt(1f - (t * t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutCirc(float t) => Mathf.Sqrt((2f - t) * t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutCirc(float t) =>
            t < 0.5f
                ? 0.5f * (1 - Mathf.Sqrt(1f - 4f * (t * t)))
                : 0.5f * (Mathf.Sqrt(-((2f * t) - 3f) * ((2f * t) - 1f)) + 1f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InCubic(float t) => t * t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutCubic(float t) => InCubic(t - 1f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutCubic(float t) =>
            t < 0.5f
                ? 4f * t * t * t
                : 0.5f * InCubic(2f * t - 2f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InElastic(float t) => Mathf.Sin(13f * (Mathf.PI * 0.5f) * t) * Mathf.Pow(2f, 10f * (t - 1f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutElastic(float t) => Mathf.Sin(-13f * (Mathf.PI * 0.5f) * (t + 1)) * Mathf.Pow(2f, -10f * t) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutElastic(float t) =>
            t < 0.5f
                ? 0.5f * Mathf.Sin(13f * (Mathf.PI * 0.5f) * (2f * t)) * Mathf.Pow(2f, 10f * ((2f * t) - 1f))
                : 0.5f * (Mathf.Sin(-13f * (Mathf.PI * 0.5f) * ((2f * t - 1f) + 1f)) * Mathf.Pow(2f, -10f * (2f * t - 1f)) + 2f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InExpo(float t) => Mathf.Approximately(0.0f, t) ? t : Mathf.Pow(2f, 10f * (t - 1f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutExpo(float t) => Mathf.Approximately(1.0f, t) ? t : 1f - Mathf.Pow(2f, -10f * t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutExpo(float v) =>
            Mathf.Approximately(0.0f, v) || Mathf.Approximately(1.0f, v)
                ? v
                : v < 0.5f
                    ? 0.5f * Mathf.Pow(2f, (20f * v) - 10f)
                    : -0.5f * Mathf.Pow(2f, (-20f * v) + 10f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InQuad(float t) => t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutQuad(float t) => -t * (t - 2f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutQuad(float t) =>
            t < 0.5f
                ? 2f * t * t
                : -2f * t * t + 4f * t - 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InQuart(float t) => t * t * t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutQuart(float t)
        {
            var u = t - 1f;
            return u * u * u * (1f - t) + 1f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutQuart(float t) =>
            t < 0.5f
                ? 8f * InQuart(t)
                : -8f * InQuart(t - 1f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InQuint(float t) => t * t * t * t * t;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutQuint(float t) => InQuint(t - 1f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutQuint(float t) =>
            t < 0.5f
                ? 16f * InQuint(t)
                : 0.5f * InQuint(2f * t - 2f) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InSine(float t) => Mathf.Sin((t - 1f) * (Mathf.PI * 0.5f)) + 1f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float OutSine(float t) => Mathf.Sin(t * (Mathf.PI * 0.5f));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InOutSine(float t) => 0.5f * (1f - Mathf.Cos(t * Mathf.PI));
    }
}
