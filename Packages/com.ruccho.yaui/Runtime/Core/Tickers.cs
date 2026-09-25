using System.Collections.Generic;
using UnityEngine;

namespace Yaui.Core
{
    /// <summary>Something that animates for a while, like the fade of a selectable's tint.</summary>
    internal interface ITicker
    {
        /// <summary>Advances to <paramref name="time"/> (unscaled seconds). Returns false when done.</summary>
        bool Tick(float time);
    }

    /// <summary>
    /// Ticks the running animations once a frame at the submission, so that idle components cost nothing per frame
    /// (no Update methods).
    /// </summary>
    internal static class Tickers
    {
        static readonly List<ITicker> Running = new();
        static readonly List<ITicker> Buffer = new();

        public static void Add(ITicker ticker)
        {
            if (!Running.Contains(ticker))
            {
                Running.Add(ticker);
            }

            YauiSystem.RequestUpdate();
        }

        public static void Remove(ITicker ticker) => Running.Remove(ticker);

        public static void Tick()
        {
            if (Running.Count == 0)
            {
                return;
            }

            // Tickers may add or remove others.
            Buffer.Clear();
            Buffer.AddRange(Running);
            var time = Time.realtimeSinceStartup;
            foreach (var ticker in Buffer)
            {
                if (!ticker.Tick(time))
                {
                    Running.Remove(ticker);
                }
            }

            if (Running.Count > 0)
            {
                YauiSystem.RequestUpdate();
            }
        }

        public static void Clear() => Running.Clear();
    }
}
