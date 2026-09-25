# YAUI (Yet Another Unity UI)

A UI system for Unity 6.7+ (URP) built on GameObjects and MonoBehaviours, aiming at better performance than uGUI:

- **One draw call per panel.** Every box, image and glyph is a quad in a structured buffer, expanded by the vertex shader. Rounded corners (per corner), borders, drop shadows, SDF text and images are one uber shader.
- **Flexbox layout** (a C# port of Yoga) on a worker thread. Fixed-size boxes are layout boundaries: changes inside them only lay out their subtree.
- **Text by the Advanced Text Generator** (shaping, OS font fallback, line breaking rules, rich text), generated on worker threads early in the frame.
- **Burst hit testing** through the EventSystem.
- **No per-frame cost when nothing changes.** Setters write the changes; uploads only send dirty chunks.

The Transform of UI GameObjects is ignored except for the hierarchy: elements are placed by the layout and moved by their render transform (which does not re-run the layout).

## Components

| Component | |
|---|---|
| `YauiPanel` | The root. Overlay (drawn after post-processing, scaled like CanvasScaler) or World space (sorted and depth tested with transparent objects). Requires a `YauiElement`. |
| `YauiElement` | A flex box with a background, per-corner radii, border and drop shadow, a render transform (translate, rotate, scale), opacity, clipping of its children (rectangle or rounded), and optionally a custom material. |
| `YauiText` | Text measured by the layout, with rich text and `<sprite>` tags (a TextCore sprite asset). Children are ignored. |
| `YauiImage` | A sprite over the content box, simple (rounded by the box radii) or 9-sliced. |
| `YauiMask` | Masks the descendants to the element's shape (the box with its radii, or an image's sprite alpha), to any depth, with the stencil buffer. Splits the draw call at its boundaries; `ClipChildren` is cheaper for one level of (rounded) rectangle clipping. |
| `YauiRaycaster` | Plugs a panel into the EventSystem. Events go to the element under the pointer and bubble up the hierarchy. |

Elements are hit when `RaycastTarget` is on and they draw something (a visible box, text or an image).

```csharp
var element = GetComponent<YauiElement>();
element.BackgroundColor = Color.red;            // no relayout, no rebuild
element.Translate = new Vector2(0f, -8f);       // render transform, no relayout
var layout = element.Layout;
layout.Width = Length.Percent(50f);             // layout properties re-run the layout (of the boundary)
element.Layout = layout;
GetComponentInChildren<YauiText>().Text = "Hello";
YauiPanel.ForceUpdate();                        // only if the layout is needed within this frame
```

## Frame pipeline

1. Setters (Update, Animator, Timeline, LateUpdate) write to the stores and mark changes.
2. Start of PreLateUpdate: changed texts start generating on worker threads.
3. Start of PostLateUpdate (the deadline): changed structures and styles are applied, the layout is scheduled on a worker thread.
4. Right before rendering: the results are applied, transforms propagated (Burst) and dirty chunks uploaded.

Changes after LateUpdate show up in the next frame. Hit tests use the state last rendered.

## Custom shaders

`YauiElement.Material` draws an element's quads with a custom material (a change of material between elements splits the draw call). The shader includes `Packages/com.ruccho.yaui/Runtime/Shaders/Yaui.hlsl` and declares an `Overlay` pass (LightMode `YauiOverlay`, vertex `VertOverlay`) and a `World` pass (vertex `VertWorld`); see the Custom Shader sample. To be masked by `YauiMask`, the shader declares the `_YauiStencil*` properties and the Stencil block of the sample. Define `YAUI_PIXEL_CLIP` before the include to clip rotated quads and rounded clips per pixel (the uber shader enables it only for panels that need it, since the extra varyings cost GPU time on mobile).

## Limitations

- URP only; Vulkan, Metal and desktop APIs (no GLES).
- Each draw call binds up to 8 textures; more split the draw. Small sprites share a dynamic atlas (`YauiTextureAtlas`).
- `ClipChildren` clips to the nearest rounded ancestor only; use `YauiMask` for deeper nesting or other shapes. Mask edges are not anti-aliased.
- The internal text APIs are called through reflection and checked at startup; if they do not match the Unity version, texts use the public `TextGenerator` on the main thread.
