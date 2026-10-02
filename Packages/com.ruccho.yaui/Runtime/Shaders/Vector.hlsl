struct VectorCurveData
{
    float2 p0;
    float2 p1;
    float2 p2;
    float weight;
    float reserved;
};

StructuredBuffer<VectorCurveData> _YauiVectorCurves;
StructuredBuffer<uint2> _YauiVectorBands;
StructuredBuffer<uint> _YauiVectorIndices;
StructuredBuffer<uint4> _YauiVectorLayers;

float VectorIntersection(VectorCurveData c, float t, bool vertical)
{
    float s = 1.0 - t;
    float3 coefficients = float3(s * s, 2.0 * c.weight * s * t, t * t);
    float3 positions = vertical ? float3(c.p0.y, c.p1.y, c.p2.y) : float3(c.p0.x, c.p1.x, c.p2.x);
    return dot(coefficients, positions) / dot(coefficients, 1.0);
}

float2 VectorRay(float2 position, uint2 band, float pixelsPerUnit, bool vertical)
{
    float coverage = 0.0;
    float weight = 0.0;
    for (uint i = 0u; i < band.y; i++)
    {
        VectorCurveData c = _YauiVectorCurves[_YauiVectorIndices[band.x + i]];
        c.p0 -= position;
        c.p1 -= position;
        c.p2 -= position;
        float3 along = vertical ? float3(c.p0.y, c.p1.y, c.p2.y) : float3(c.p0.x, c.p1.x, c.p2.x);
        if (max(along.x, max(along.y, along.z)) * pixelsPerUnit < -0.5) break;
        float3 across = vertical ? float3(c.p0.x, c.p1.x, c.p2.x) : float3(c.p0.y, c.p1.y, c.p2.y);
        uint shift = (across.x > 0.0 ? 2u : 0u) + (across.y > 0.0 ? 4u : 0u) + (across.z > 0.0 ? 8u : 0u);
        uint code = (0x2E74u >> shift) & 3u;
        if (code == 0u) continue;
        float a = across.x - 2.0 * c.weight * across.y + across.z;
        float b = across.x - c.weight * across.y;
        float discriminant = sqrt(max(b * b - a * across.x, 0.0));
        float2 roots;
        if (abs(a) < 1e-6)
            roots = across.x / (2.0 * b);
        else
            roots = float2(b - discriminant, b + discriminant) / a;
        if (code & 1u)
        {
            float distance = VectorIntersection(c, roots.x, vertical) * pixelsPerUnit;
            coverage += saturate(distance + 0.5);
            weight = max(weight, saturate(1.0 - abs(distance) * 2.0));
        }
        if (code & 2u)
        {
            float distance = VectorIntersection(c, roots.y, vertical) * pixelsPerUnit;
            coverage -= saturate(distance + 0.5);
            weight = max(weight, saturate(1.0 - abs(distance) * 2.0));
        }
    }

    return float2(vertical ? -coverage : coverage, weight);
}

float VectorCoverage(float2 position, uint layerIndex)
{
    uint4 layer = _YauiVectorLayers[layerIndex];
    uint count = layer.y;
    uint2 bandIndex = (uint2)clamp(floor(position.yx * count), 0.0, (float)count - 1.0);
    float2 pixels = 1.0 / max(fwidth(position), 1e-7);
    float2 horizontal = VectorRay(position, _YauiVectorBands[layer.x + bandIndex.x], pixels.x, false);
    float2 vertical = VectorRay(position, _YauiVectorBands[layer.x + count + bandIndex.y], pixels.y, true);
    if (layer.z != 0u)
    {
        horizontal.x = 1.0 - abs(1.0 - fmod(abs(horizontal.x), 2.0));
        vertical.x = 1.0 - abs(1.0 - fmod(abs(vertical.x), 2.0));
    }
    float coverage = abs((horizontal.x * horizontal.y + vertical.x * vertical.y) /
        max(horizontal.y + vertical.y, 1e-7));
    return saturate(max(coverage, min(abs(horizontal.x), abs(vertical.x))));
}
