// Vendored for YAUI from microsoft/microsoft-ui-reactor (MIT, see LICENSE-Reactor.txt)
// src/Reactor/Yoga/YogaConfig.cs at a58008a1be0d, itself a C# port of Meta's Yoga (MIT).
// Changes: namespace; YAUI uses one fixed configuration (the defaults), so the config is a constant struct
// (unmanaged, for Burst).

// C# port of Meta's Yoga layout engine Config.
// Ported from yoga/config/Config.h, yoga/config/Config.cpp

namespace Yaui.Layout.Yoga
{

internal readonly struct YogaConfig
{
    public static YogaConfig Default => default;

    public bool UseWebDefaults => false;

    public float PointScaleFactor => 1.0f;

    public uint Version => 0;

    public bool IsExperimentalFeatureEnabled(YogaExperimentalFeature feature) => false;

    public YogaErrata Errata => YogaErrata.None;

    public bool HasErrata(YogaErrata errata) => (Errata & errata) != YogaErrata.None;
}
}
