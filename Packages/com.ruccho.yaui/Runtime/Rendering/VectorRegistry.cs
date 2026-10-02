using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Yaui.Core;

namespace Yaui.Rendering
{
    internal struct VectorCurveGpu
    {
        public float2 P0;
        public float2 P1;
        public float2 P2;
        public float Weight;
        public float Reserved;
    }

    internal struct VectorLayerGpu
    {
        public uint BandStart;
        public uint BandCount;
        public uint EvenOdd;
        public uint Reserved;
    }

    internal sealed class VectorRegistry : IDisposable
    {
        private sealed class Entry
        {
            public YauiVectorAsset Asset;
            public int References;
            public int[] Layers;
            public (int Curves, int CurveCapacity, int Bands, int BandCapacity, int Indices, int IndexCapacity)[] Ranges;
        }

        private readonly Dictionary<YauiVectorAsset, Entry> _assets = new();
        private readonly Dictionary<int, Entry> _leases = new();
        private int _nextLease;
        internal readonly GpuStore<VectorCurveGpu> Curves = new(64, 1);
        internal readonly GpuStore<uint2> Bands = new(64, 1);
        internal readonly GpuStore<uint> Indices = new(64, 1);
        internal readonly GpuStore<VectorLayerGpu> Layers = new(16, 1);

        internal int Acquire(YauiVectorAsset asset)
        {
            if (!_assets.TryGetValue(asset, out var entry))
            {
                entry = new Entry
                {
                    Asset = asset, Layers = new int[asset.Layers.Count],
                    Ranges = new (int, int, int, int, int, int)[asset.Layers.Count]
                };
                for (var i = 0; i < asset.Layers.Count; i++)
                {
                    var layer = asset.Layers[i];
                    var cc = GpuStore<VectorCurveGpu>.RangeCapacity(layer.Curves.Count);
                    var bc = GpuStore<uint2>.RangeCapacity(layer.Bands.Count);
                    var ic = GpuStore<uint>.RangeCapacity(layer.Indices.Count);
                    var cs = Curves.AllocateRange(cc);
                    var bs = Bands.AllocateRange(bc);
                    var indexStart = Indices.AllocateRange(ic);
                    var slot = Layers.Allocate();
                    entry.Layers[i] = slot;
                    entry.Ranges[i] = (cs, cc, bs, bc, indexStart, ic);
                    for (var c = 0; c < layer.Curves.Count; c++)
                    {
                        var curve = layer.Curves[c];
                        Curves[cs + c] = new VectorCurveGpu
                        {
                            P0 = curve.P0, P1 = curve.P1, P2 = curve.P2, Weight = curve.Weight, Reserved = 0f
                        };
                    }

                    for (var b = 0; b < layer.Bands.Count; b++)
                    {
                        var band = layer.Bands[b];
                        Bands[bs + b] = new uint2((uint)(indexStart + band.Start), (uint)band.Count);
                    }

                    for (var c = 0; c < layer.Indices.Count; c++)
                        Indices[indexStart + c] = (uint)(cs + layer.Indices[c]);
                    Layers[slot] = new VectorLayerGpu
                    {
                        BandStart = (uint)bs, BandCount = (uint)layer.BandCount, EvenOdd = layer.EvenOdd ? 1u : 0u, Reserved = 0u
                    };
                }

                _assets.Add(asset, entry);
            }

            entry.References++;
            var lease = checked(++_nextLease);
            _leases.Add(lease, entry);
            return lease;
        }

        internal bool Contains(int lease) => _leases.ContainsKey(lease);
        internal YauiVectorAsset Asset(int lease) => _leases[lease].Asset;
        internal int Layer(int lease, int layer) => _leases[lease].Layers[layer];

        internal void Release(int lease)
        {
            if (!_leases.Remove(lease, out var entry)) return;
            if (--entry.References != 0) return;
            _assets.Remove(entry.Asset);
            for (var i = 0; i < entry.Layers.Length; i++)
            {
                var range = entry.Ranges[i];
                Curves.FreeRange(range.Curves, range.CurveCapacity);
                Bands.FreeRange(range.Bands, range.BandCapacity);
                Indices.FreeRange(range.Indices, range.IndexCapacity);
                Layers.Free(entry.Layers[i]);
            }
        }

        internal void Upload()
        {
            Curves.Upload();
            Bands.Upload();
            Indices.Upload();
            Layers.Upload();
        }

        public void Dispose()
        {
            _leases.Clear();
            _assets.Clear();
            Curves.Dispose();
            Bands.Dispose();
            Indices.Dispose();
            Layers.Dispose();
        }
    }
}
