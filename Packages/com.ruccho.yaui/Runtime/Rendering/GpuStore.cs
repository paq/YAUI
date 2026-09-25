using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Yaui.Rendering
{
    /// <summary>
    /// Records mirrored to a structured buffer. Slots are allocated and freed (reused through a free list) and never
    /// move. Writes mark 64-record chunks dirty, and <see cref="Upload"/> sends only the dirty chunks.
    /// </summary>
    internal sealed class GpuStore<T> : IDisposable where T : unmanaged
    {
        public const int ChunkShift = 6;

        NativeList<T> items;
        NativeList<int> free;

        // Free blocks of AllocateRange by capacity.
        readonly Dictionary<int, Stack<int>> freeRanges = new();

        // One bit per chunk.
        NativeList<ulong> dirtyChunks;
        GraphicsBuffer buffer;

        /// <param name="reserved">Slots at the start that are never allocated, such as index 0 meaning "none".</param>
        public GpuStore(int capacity, int reserved = 0)
        {
            items = new NativeList<T>(Math.Max(capacity, reserved + 1), Allocator.Persistent);
            free = new NativeList<int>(64, Allocator.Persistent);
            dirtyChunks = new NativeList<ulong>(16, Allocator.Persistent);
            for (var i = 0; i < reserved; i++)
            {
                items.Add(default);
            }

            EnsureDirtyCapacity();
            MarkAllDirty();
        }

        public int Length => items.Length;

        public GraphicsBuffer Buffer => buffer;

        /// <summary>Records by slot, for jobs. Valid until the next <see cref="Allocate"/>.</summary>
        public NativeArray<T> AsArray() => items.AsArray();

        /// <summary>Dirty chunk bits, for jobs that write records. Valid until the next <see cref="Allocate"/>.</summary>
        public NativeArray<ulong> DirtyChunks => dirtyChunks.AsArray();

        public int Allocate()
        {
            int slot;
            if (free.Length > 0)
            {
                slot = free[free.Length - 1];
                free.RemoveAt(free.Length - 1);
                items[slot] = default;
            }
            else
            {
                slot = items.Length;
                items.Add(default);
                EnsureDirtyCapacity();
            }

            MarkDirty(slot);
            return slot;
        }

        /// <summary>Rounds a count up to the capacity of a block (a power of two, at least 4).</summary>
        public static int RangeCapacity(int count) => Math.Max(4, (int)Unity.Mathematics.math.ceilpow2((uint)count));

        /// <summary>Allocates <paramref name="capacity"/> contiguous slots (see <see cref="RangeCapacity"/>).</summary>
        public int AllocateRange(int capacity)
        {
            if (freeRanges.TryGetValue(capacity, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            var start = items.Length;
            for (var i = 0; i < capacity; i++)
            {
                items.Add(default);
            }

            EnsureDirtyCapacity();
            for (var i = 0; i < capacity; i += 1 << ChunkShift)
            {
                MarkDirty(start + i);
            }

            MarkDirty(start + capacity - 1);
            return start;
        }

        public void FreeRange(int start, int capacity)
        {
            for (var i = 0; i < capacity; i++)
            {
                items[start + i] = default;
                MarkDirty(start + i);
            }

            if (!freeRanges.TryGetValue(capacity, out var stack))
            {
                freeRanges[capacity] = stack = new Stack<int>();
            }

            stack.Push(start);
        }

        public void Free(int slot)
        {
            items[slot] = default;
            MarkDirty(slot);
            free.Add(slot);
        }

        /// <summary>Returns the record for writing and marks its chunk dirty.</summary>
        public ref T this[int slot]
        {
            get
            {
                MarkDirty(slot);
                return ref items.ElementAt(slot);
            }
        }

        public T Read(int slot) => items[slot];

        public void MarkDirty(int slot) => MarkDirty(dirtyChunks.AsArray(), slot);

        /// <summary>Marks the chunk of <paramref name="slot"/> dirty. Usable from jobs with <see cref="DirtyChunks"/>.</summary>
        public static void MarkDirty(NativeArray<ulong> dirtyChunks, int slot)
        {
            var chunk = slot >> ChunkShift;
            dirtyChunks[chunk >> 6] |= 1ul << (chunk & 63);
        }

        void MarkAllDirty()
        {
            for (var i = 0; i < dirtyChunks.Length; i++)
            {
                dirtyChunks[i] = ~0ul;
            }
        }

        void EnsureDirtyCapacity()
        {
            var chunks = (items.Length + (1 << ChunkShift) - 1) >> ChunkShift;
            var words = (chunks + 63) >> 6;
            while (dirtyChunks.Length < words)
            {
                dirtyChunks.Add(0);
            }
        }

        /// <summary>Sends the dirty chunks, recreating the buffer if it is too small. Returns the buffer.</summary>
        public GraphicsBuffer Upload()
        {
            if (buffer == null || buffer.count < items.Length)
            {
                buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Math.Max(items.Capacity, 64),
                    UnsafeUtility.SizeOf<T>());
                MarkAllDirty();
            }

            var array = items.AsArray();
            var chunkSize = 1 << ChunkShift;
            var runStart = -1;
            for (var word = 0; word < dirtyChunks.Length; word++)
            {
                var bits = dirtyChunks[word];
                if (bits == 0ul && runStart < 0)
                {
                    continue;
                }

                for (var bit = 0; bit < 64; bit++)
                {
                    var chunk = (word << 6) + bit;
                    var dirty = (bits & (1ul << bit)) != 0;
                    if (dirty && runStart < 0)
                    {
                        runStart = chunk;
                    }
                    else if (!dirty && runStart >= 0)
                    {
                        UploadRun(array, runStart * chunkSize, chunk * chunkSize);
                        runStart = -1;
                    }
                }

                dirtyChunks[word] = 0ul;
            }

            if (runStart >= 0)
            {
                UploadRun(array, runStart * chunkSize, array.Length);
            }

            return buffer;
        }

        void UploadRun(NativeArray<T> array, int start, int end)
        {
            end = Math.Min(end, array.Length);
            if (end > start)
            {
                buffer.SetData(array, start, start, end - start);
            }
        }

        public void Dispose()
        {
            buffer?.Dispose();
            buffer = null;
            if (items.IsCreated)
            {
                items.Dispose();
                free.Dispose();
                dirtyChunks.Dispose();
            }
        }
    }
}
