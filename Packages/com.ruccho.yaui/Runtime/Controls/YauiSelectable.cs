using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using Yaui.Core;

namespace Yaui
{
    public enum SelectionState
    {
        Normal,
        Highlighted,
        Pressed,
        Selected,
        Disabled
    }

    public enum SelectableTransition
    {
        None,

        /// <summary>Multiplies the target's colors (<see cref="YauiElement.Tint"/>), fading between states.</summary>
        ColorTint,

        /// <summary>Draws another sprite on the target image (<see cref="YauiImage.OverrideSprite"/>).</summary>
        SpriteSwap,

        /// <summary>Sets a trigger of the Animator on the selectable's GameObject.</summary>
        Animation
    }

    [Serializable]
    public struct TransitionColors
    {
        public Color Normal;
        public Color Highlighted;
        public Color Pressed;
        public Color Selected;
        public Color Disabled;

        [Range(1f, 5f)] public float ColorMultiplier;

        /// <summary>Seconds to fade from one color to the next (unscaled time).</summary>
        public float FadeDuration;

        public static TransitionColors Default => new()
        {
            Normal = Color.white,
            Highlighted = new Color32(245, 245, 245, 255),
            Pressed = new Color32(200, 200, 200, 255),
            Selected = new Color32(245, 245, 245, 255),
            Disabled = new Color32(200, 200, 200, 128),
            ColorMultiplier = 1f,
            FadeDuration = 0.1f
        };
    }

    /// <summary>The sprites of the states besides normal (the target image's own sprite). Null keeps the own sprite.</summary>
    [Serializable]
    public struct TransitionSprites
    {
        public Sprite Highlighted;
        public Sprite Pressed;
        public Sprite Selected;
        public Sprite Disabled;
    }

    [Serializable]
    public struct TransitionTriggers
    {
        public string Normal;
        public string Highlighted;
        public string Pressed;
        public string Selected;
        public string Disabled;

        public static TransitionTriggers Default => new()
        {
            Normal = "Normal",
            Highlighted = "Highlighted",
            Pressed = "Pressed",
            Selected = "Selected",
            Disabled = "Disabled"
        };
    }

    public enum NavigationMode
    {
        None,

        /// <summary>Automatic, left and right only.</summary>
        Horizontal,

        /// <summary>Automatic, up and down only.</summary>
        Vertical,

        /// <summary>The nearest selectable of the panel in the direction of the move.</summary>
        Automatic,

        /// <summary>The selectables set for each direction.</summary>
        Explicit
    }

    [Serializable]
    public struct SelectableNavigation
    {
        public NavigationMode Mode;

        /// <summary>Automatic modes: moving past the last selectable in a direction selects the first.</summary>
        public bool WrapAround;

        /// <summary>Explicit mode: the selectables of each direction. Other modes use them where set.</summary>
        public YauiSelectable Up;

        public YauiSelectable Down;
        public YauiSelectable Left;
        public YauiSelectable Right;

        public static SelectableNavigation Default => new() { Mode = NavigationMode.Automatic };
    }

    /// <summary>
    /// The base of controls: tracks the pointer and the EventSystem's selection, shows the state with a transition
    /// on a target element (like uGUI's Selectable), and moves the selection with the navigation events (gamepad,
    /// keyboard). Sits next to a <see cref="YauiElement"/>; events on child elements bubble up to it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(YauiElement))]
    public abstract class YauiSelectable : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler, IMoveHandler, ITicker
    {
        private static readonly List<YauiSelectable> All = new();

        [SerializeField] private bool interactable = true;
        [SerializeField] private SelectableTransition transition = SelectableTransition.ColorTint;

        /// <summary>The element the transition applies to; this GameObject's element when null.</summary>
        [SerializeField] private YauiElement targetElement;

        [SerializeField] private TransitionColors colors = TransitionColors.Default;
        [SerializeField] private TransitionSprites sprites;
        [SerializeField] private TransitionTriggers triggers = TransitionTriggers.Default;
        [SerializeField] private SelectableNavigation navigation = SelectableNavigation.Default;

        [NonSerialized] private YauiElement element;
        [NonSerialized] private bool pointerInside;
        [NonSerialized] private bool pointerDown;
        [NonSerialized] private bool selected;
        [NonSerialized] private SelectionState appliedState = (SelectionState)(-1);

        // The tint fade.
        [NonSerialized] private Color fadeFrom;
        [NonSerialized] private Color fadeTo;
        [NonSerialized] private float fadeStart;
        [NonSerialized] private float fadeDuration;
        [NonSerialized] private YauiElement fadeTarget;

        // Until when a submit shows the pressed state (unscaled seconds), or 0.
        [NonSerialized] private float flashUntil;

        /// <summary>All enabled selectables.</summary>
        public static IReadOnlyList<YauiSelectable> AllSelectables => All;

        /// <summary>The element of this GameObject.</summary>
        public YauiElement Element => element != null ? element : element = GetComponent<YauiElement>();

        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value) return;

                interactable = value;
                if (!interactable && EventSystem.current != null &&
                    EventSystem.current.currentSelectedGameObject == gameObject)
                    EventSystem.current.SetSelectedGameObject(null);

                OnInteractableChanged();
                UpdateState(false);
            }
        }

        public SelectableTransition Transition
        {
            get => transition;
            set
            {
                ClearTransition();
                transition = value;
                UpdateState(true);
            }
        }

        public YauiElement TargetElement
        {
            get => targetElement != null ? targetElement : Element;
            set
            {
                ClearTransition();
                targetElement = value;
                UpdateState(true);
            }
        }

        public TransitionColors Colors
        {
            get => colors;
            set
            {
                colors = value;
                UpdateState(false);
            }
        }

        public TransitionSprites Sprites
        {
            get => sprites;
            set
            {
                sprites = value;
                UpdateState(true);
            }
        }

        public TransitionTriggers Triggers
        {
            get => triggers;
            set => triggers = value;
        }

        public SelectableNavigation Navigation
        {
            get => navigation;
            set => navigation = value;
        }

        /// <summary>Whether the selectable reacts: interactable, enabled and active.</summary>
        public bool IsInteractable => interactable && isActiveAndEnabled;

        public SelectionState CurrentState
        {
            get
            {
                if (!IsInteractable) return SelectionState.Disabled;

                if (pointerDown || flashUntil > 0f) return SelectionState.Pressed;

                if (selected) return SelectionState.Selected;

                return pointerInside ? SelectionState.Highlighted : SelectionState.Normal;
            }
        }

        /// <summary>Whether the pointer that is down went down on this selectable.</summary>
        protected bool IsPressed => IsInteractable && pointerDown;

        protected virtual void OnEnable()
        {
            All.Add(this);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject)
                selected = true;

            UpdateState(true);
        }

        protected virtual void OnDisable()
        {
            All.Remove(this);
            pointerInside = false;
            pointerDown = false;
            selected = false;
            flashUntil = 0f;
            ClearTransition();
        }

        protected virtual void OnValidate()
        {
            colors.FadeDuration = Mathf.Max(colors.FadeDuration, 0f);
            if (isActiveAndEnabled) UpdateState(true);
        }

        /// <summary>Makes this the EventSystem's selected object.</summary>
        public virtual void Select()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null && !eventSystem.alreadySelecting) eventSystem.SetSelectedGameObject(gameObject);
        }

        protected virtual void OnInteractableChanged()
        {
        }

        /// <summary>Shows the pressed state for a moment, for presses without a pointer (submit).</summary>
        protected void FlashPressed()
        {
            if (!IsInteractable || !Application.isPlaying) return;

            flashUntil = Time.realtimeSinceStartup + Mathf.Max(colors.FadeDuration, 0.05f);
            UpdateState(false);
            Tickers.Add(this);
        }

        #region Events

        public virtual void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            UpdateState(false);
        }

        public virtual void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            UpdateState(false);
        }

        public virtual void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            // Pointer presses select, unless the selectable takes no part in the navigation.
            if (IsInteractable && navigation.Mode != NavigationMode.None && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(gameObject, eventData);

            pointerDown = true;
            UpdateState(false);
        }

        public virtual void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            pointerDown = false;
            UpdateState(false);
        }

        public virtual void OnSelect(BaseEventData eventData)
        {
            selected = true;
            UpdateState(false);
        }

        public virtual void OnDeselect(BaseEventData eventData)
        {
            selected = false;
            UpdateState(false);
        }

        public virtual void OnMove(AxisEventData eventData)
        {
            var next = eventData.moveDir switch
            {
                MoveDirection.Left => FindSelectableOnLeft(),
                MoveDirection.Right => FindSelectableOnRight(),
                MoveDirection.Up => FindSelectableOnUp(),
                MoveDirection.Down => FindSelectableOnDown(),
                _ => null
            };

            if (next != null && next.IsInteractable) eventData.selectedObject = next.gameObject;
        }

        #endregion

        #region Navigation

        public virtual YauiSelectable FindSelectableOnLeft()
        {
            return navigation.Left != null || navigation.Mode == NavigationMode.Explicit
                ? navigation.Left
                : navigation.Mode is NavigationMode.Automatic or NavigationMode.Horizontal
                    ? FindSelectable(new float2(-1f, 0f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnRight()
        {
            return navigation.Right != null || navigation.Mode == NavigationMode.Explicit
                ? navigation.Right
                : navigation.Mode is NavigationMode.Automatic or NavigationMode.Horizontal
                    ? FindSelectable(new float2(1f, 0f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnUp()
        {
            return navigation.Up != null || navigation.Mode == NavigationMode.Explicit
                ? navigation.Up
                : navigation.Mode is NavigationMode.Automatic or NavigationMode.Vertical
                    ? FindSelectable(new float2(0f, -1f))
                    : null;
        }

        public virtual YauiSelectable FindSelectableOnDown()
        {
            return navigation.Down != null || navigation.Mode == NavigationMode.Explicit
                ? navigation.Down
                : navigation.Mode is NavigationMode.Automatic or NavigationMode.Vertical
                    ? FindSelectable(new float2(0f, 1f))
                    : null;
        }

        /// <summary>
        /// The selectable of the same panel that is nearest in <paramref name="direction"/> (canvas space, Y down),
        /// weighted toward the direction like uGUI: the score is the distance along the direction over the squared
        /// distance.
        /// </summary>
        public YauiSelectable FindSelectable(Vector2 direction)
        {
            var dir = math.normalizesafe((float2)direction);
            if (!TryGetCanvasBounds(this, out var own, out var panel)) return null;

            // Starts from the edge of the own box in the direction, so that overlapping boxes still order.
            var origin = own.Center + dir * own.HalfSize;
            YauiSelectable best = null;
            var bestScore = float.NegativeInfinity;
            YauiSelectable farthest = null;
            var farthestDistance = float.NegativeInfinity;
            foreach (var candidate in All)
            {
                if (candidate == this || !candidate.IsInteractable ||
                    candidate.navigation.Mode == NavigationMode.None ||
                    !TryGetCanvasBounds(candidate, out var bounds, out var candidatePanel) || candidatePanel != panel)
                    continue;

                var toCandidate = bounds.Center - origin;
                var along = math.dot(dir, toCandidate);
                if (along > 0f)
                {
                    var score = along / math.max(math.lengthsq(toCandidate), 1e-6f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
                else if (navigation.WrapAround)
                {
                    // Wrapping around goes to the farthest one on the other side.
                    var distance = -math.dot(dir, bounds.Center - own.Center);
                    if (distance > farthestDistance)
                    {
                        farthestDistance = distance;
                        farthest = candidate;
                    }
                }
            }

            return best != null ? best : farthest;
        }

        private struct CanvasBounds
        {
            public float2 Center;
            public float2 HalfSize;
        }

        private static bool TryGetCanvasBounds(YauiSelectable selectable, out CanvasBounds bounds, out object panel)
        {
            bounds = default;
            panel = null;
            var e = selectable.Element;
            if (e == null || e.NodeSlot <= 0) return false;

            var state = e.PanelState;
            if (state == null || !state.TryGetWorld(e, out var world)) return false;

            var size = (float2)e.LayoutRect.size;
            var center = world.c0 * (size.x * 0.5f) + world.c1 * (size.y * 0.5f) + world.c2;
            var half = math.abs(world.c0) * (size.x * 0.5f) + math.abs(world.c1) * (size.y * 0.5f);
            bounds = new CanvasBounds { Center = center, HalfSize = half };
            panel = state;
            return true;
        }

        #endregion

        #region Transitions

        /// <summary>Shows the current state. Instant skips the fade.</summary>
        protected void UpdateState(bool instant)
        {
            if (!isActiveAndEnabled) return;

            var state = CurrentState;
            if (state == appliedState && !instant) return;

            appliedState = state;
            ApplyTransition(state, instant);
        }

        protected virtual void ApplyTransition(SelectionState state, bool instant)
        {
            var target = TargetElement;
            switch (transition)
            {
                case SelectableTransition.ColorTint:
                    FadeTint(target, ColorOf(state) * colors.ColorMultiplier, instant ? 0f : colors.FadeDuration);
                    break;
                case SelectableTransition.SpriteSwap:
                    if (target is YauiImage image)
                        image.OverrideSprite = state switch
                        {
                            SelectionState.Highlighted => sprites.Highlighted,
                            SelectionState.Pressed => sprites.Pressed,
                            SelectionState.Selected => sprites.Selected,
                            SelectionState.Disabled => sprites.Disabled,
                            _ => null
                        };

                    break;
                case SelectableTransition.Animation:
                    SetTrigger(state switch
                    {
                        SelectionState.Highlighted => triggers.Highlighted,
                        SelectionState.Pressed => triggers.Pressed,
                        SelectionState.Selected => triggers.Selected,
                        SelectionState.Disabled => triggers.Disabled,
                        _ => triggers.Normal
                    });
                    break;
            }
        }

        private Color ColorOf(SelectionState state)
        {
            return state switch
            {
                SelectionState.Highlighted => colors.Highlighted,
                SelectionState.Pressed => colors.Pressed,
                SelectionState.Selected => colors.Selected,
                SelectionState.Disabled => colors.Disabled,
                _ => colors.Normal
            };
        }

        private void FadeTint(YauiElement target, Color color, float duration)
        {
            if (target == null) return;

            if (duration <= 0f || !Application.isPlaying)
            {
                fadeTarget = null;
                target.Tint = color;
                return;
            }

            fadeTarget = target;
            fadeFrom = target.Tint;
            fadeTo = color;
            fadeStart = Time.realtimeSinceStartup;
            fadeDuration = duration;
            Tickers.Add(this);
        }

        bool ITicker.Tick(float time)
        {
            if (flashUntil > 0f && time >= flashUntil)
            {
                flashUntil = 0f;
                UpdateState(false);
            }

            var running = flashUntil > 0f;
            if (fadeTarget != null)
            {
                var t = Mathf.Clamp01((time - fadeStart) / fadeDuration);
                fadeTarget.Tint = Color.Lerp(fadeFrom, fadeTo, t);
                if (t < 1f)
                    running = true;
                else
                    fadeTarget = null;
            }

            return running;
        }

        private void SetTrigger(string trigger)
        {
            if (!Application.isPlaying || !TryGetComponent<Animator>(out var animator) ||
                !animator.isActiveAndEnabled ||
                animator.runtimeAnimatorController == null || string.IsNullOrEmpty(trigger))
                return;

            animator.ResetTrigger(triggers.Normal);
            animator.ResetTrigger(triggers.Highlighted);
            animator.ResetTrigger(triggers.Pressed);
            animator.ResetTrigger(triggers.Selected);
            animator.ResetTrigger(triggers.Disabled);
            animator.SetTrigger(trigger);
        }

        /// <summary>Removes what the transition applied to the target.</summary>
        private void ClearTransition()
        {
            Tickers.Remove(this);
            fadeTarget = null;
            appliedState = (SelectionState)(-1);
            var target = TargetElement;
            if (target == null) return;

            if (transition == SelectableTransition.ColorTint)
                target.Tint = Color.white;
            else if (transition == SelectableTransition.SpriteSwap && target is YauiImage image)
                image.OverrideSprite = null;
        }

        #endregion
    }
}