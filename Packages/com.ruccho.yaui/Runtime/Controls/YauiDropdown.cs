using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Yaui.Core;

namespace Yaui
{
    /// <summary>
    /// A selectable that picks one of its options from a list, like uGUI's Dropdown. The list is a copy of
    /// <see cref="Template"/> (an inactive element, typically a scroll view) shown on top of the panel below the
    /// dropdown, with a copy of the template's item (the toggle around <see cref="ItemText"/>) for each option.
    /// Presses outside the list close it.
    /// </summary>
    [AddComponentMenu("YAUI/Dropdown")]
    public class YauiDropdown : YauiSelectable, IPointerClickHandler, ISubmitHandler, ICancelHandler
    {
        [Serializable]
        public class OptionData
        {
            public string Text;
            public Sprite Image;

            public OptionData()
            {
            }

            public OptionData(string text, Sprite image = null)
            {
                Text = text;
                Image = image;
            }
        }

        [Serializable]
        public class DropdownEvent : UnityEvent<int>
        {
        }

        /// <summary>The list, inactive: an element with the item somewhere inside.</summary>
        [SerializeField] YauiElement template;

        /// <summary>Shows the text of the selected option.</summary>
        [SerializeField] YauiText captionText;

        /// <summary>Shows the image of the selected option, if any.</summary>
        [SerializeField] YauiImage captionImage;

        /// <summary>The text of the item in the template. The toggle around it is the item.</summary>
        [SerializeField] YauiText itemText;

        [SerializeField] YauiImage itemImage;
        [SerializeField] List<OptionData> options = new();
        [SerializeField] int value;
        [SerializeField] DropdownEvent onValueChanged = new();

        [NonSerialized] GameObject list;
        [NonSerialized] GameObject blocker;
        [NonSerialized] readonly List<YauiToggle> items = new();

        public YauiElement Template
        {
            get => template;
            set => template = value;
        }

        public YauiText CaptionText
        {
            get => captionText;
            set
            {
                captionText = value;
                RefreshShownValue();
            }
        }

        public YauiImage CaptionImage
        {
            get => captionImage;
            set
            {
                captionImage = value;
                RefreshShownValue();
            }
        }

        public YauiText ItemText
        {
            get => itemText;
            set => itemText = value;
        }

        public YauiImage ItemImage
        {
            get => itemImage;
            set => itemImage = value;
        }

        /// <summary>The options. Call <see cref="RefreshShownValue"/> after changing them in place.</summary>
        public List<OptionData> Options
        {
            get => options;
            set
            {
                options = value ?? new List<OptionData>();
                RefreshShownValue();
            }
        }

        /// <summary>The index of the selected option.</summary>
        public int Value
        {
            get => value;
            set => Set(value, true);
        }

        public DropdownEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new DropdownEvent();
        }

        /// <summary>The shown list and its items (tests).</summary>
        internal GameObject ListObject => list;

        internal IReadOnlyList<YauiToggle> Items => items;

        internal GameObject BlockerObject => blocker;

        /// <summary>Whether the list is shown.</summary>
        public bool IsExpanded => list != null;

        public void SetValueWithoutNotify(int input) => Set(input, false);

        public void AddOptions(IEnumerable<string> texts)
        {
            foreach (var text in texts)
            {
                options.Add(new OptionData(text));
            }

            RefreshShownValue();
        }

        public void AddOptions(IEnumerable<OptionData> data)
        {
            options.AddRange(data);
            RefreshShownValue();
        }

        public void ClearOptions()
        {
            options.Clear();
            value = 0;
            RefreshShownValue();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshShownValue();
        }

        protected override void OnDisable()
        {
            Hide();
            base.OnDisable();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            if (isActiveAndEnabled)
            {
                RefreshShownValue();
            }
        }

        void Set(int input, bool notify)
        {
            var clamped = options.Count == 0 ? 0 : Mathf.Clamp(input, 0, options.Count - 1);
            if (clamped == value)
            {
                return;
            }

            value = clamped;
            RefreshShownValue();
            if (notify)
            {
                onValueChanged.Invoke(value);
            }
        }

        /// <summary>Shows the selected option in the caption.</summary>
        public void RefreshShownValue()
        {
            var option = value >= 0 && value < options.Count ? options[value] : null;
            if (captionText != null)
            {
                captionText.Text = option?.Text ?? "";
            }

            if (captionImage != null)
            {
                captionImage.Sprite = option?.Image;
                captionImage.Opacity = option?.Image != null ? 1f : 0f;
            }
        }

        #region Events

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Show();
            }
        }

        public virtual void OnSubmit(BaseEventData eventData) => Show();

        public virtual void OnCancel(BaseEventData eventData) => Hide();

        #endregion

        /// <summary>Shows the list below the dropdown (above it if there is no room below).</summary>
        public void Show()
        {
            if (list != null || !IsInteractable || template == null || itemText == null ||
                !itemText.transform.IsChildOf(template.transform))
            {
                return;
            }

            var state = Element.PanelState;
            if (state == null || state.Panel == null || !state.TryGetWorld(Element, out var world))
            {
                return;
            }

            var panel = state.Panel;
            var root = panel.Element;
            var size = Element.LayoutRect.size;

            // Behind the list, a blocker takes the presses outside it.
            blocker = new GameObject("Dropdown Blocker") { hideFlags = HideFlags.HideAndDontSave };
            blocker.transform.SetParent(root.transform, false);
            var blockerElement = blocker.AddComponent<DropdownBlocker>();
            blockerElement.Dropdown = this;
            var blockerLayout = blockerElement.Layout;
            blockerLayout.Position = PositionType.Absolute;
            blockerLayout.Inset = new Edges(Length.Points(0f));
            blockerElement.Layout = blockerLayout;

            // The list is a copy of the template at the end of the panel, drawn on top of everything.
            list = Instantiate(template.gameObject, root.transform, false);
            list.name = "Dropdown List";
            list.hideFlags = HideFlags.HideAndDontSave;
            var listElement = list.GetComponent<YauiElement>();

            // Canvas position of the dropdown's bottom-left, in the root's padding box.
            var border = root.Box.BorderWidth;
            var topLeft = (Vector2)world.c2 - new Vector2(border, border);
            var bottom = topLeft.y + ((Vector2)world.c1).y * size.y;
            var layout = listElement.Layout;
            layout.Position = PositionType.Absolute;
            if (layout.Width.Unit == LengthUnit.Auto)
            {
                layout.Width = Length.Points(((Vector2)world.c0).x * size.x);
            }

            var height = layout.Height.Unit == LengthUnit.Point ? layout.Height.Value : 0f;
            var above = height > 0f && bottom + height > panel.CanvasSize.y && topLeft.y - height >= 0f;
            layout.Inset = new Edges(Length.Points(topLeft.x), Length.Points(above ? topLeft.y - height : bottom),
                Length.Auto, Length.Auto);
            listElement.Layout = layout;

            // The item: the toggle around the item text, copied for each option.
            var itemTextCopy = Corresponding(itemText, template.transform, list.transform);
            var itemImageCopy = itemImage != null && itemImage.transform.IsChildOf(template.transform)
                ? Corresponding(itemImage, template.transform, list.transform)
                : null;
            var itemToggle = itemTextCopy != null ? itemTextCopy.GetComponentInParent<YauiToggle>(true) : null;
            if (itemToggle == null)
            {
                Debug.LogWarning("[YAUI] The item text of the dropdown template must be inside a toggle.", this);
                Hide();
                return;
            }

            var itemObject = itemToggle.gameObject;
            for (var i = 0; i < options.Count; i++)
            {
                var copy = Instantiate(itemObject, itemObject.transform.parent, false);
                copy.name = $"Item {i}: {options[i].Text}";
                var toggle = copy.GetComponent<YauiToggle>();
                var text = Corresponding(itemTextCopy, itemObject.transform, copy.transform);
                if (text != null)
                {
                    text.Text = options[i].Text;
                }

                if (itemImageCopy != null)
                {
                    var image = Corresponding(itemImageCopy, itemObject.transform, copy.transform);
                    if (image != null)
                    {
                        image.Sprite = options[i].Image;
                        image.Opacity = options[i].Image != null ? 1f : 0f;
                    }
                }

                toggle.Group = null;
                toggle.SetIsOnWithoutNotify(i == value);
                var index = i;
                toggle.OnValueChanged.AddListener(_ => OnItemSelected(index));
                copy.AddComponent<DropdownItem>().Dropdown = this;
                copy.SetActive(true);
                items.Add(toggle);
            }

            itemObject.SetActive(false);

            // The keys move through the items, in order.
            for (var i = 0; i < items.Count; i++)
            {
                items[i].Navigation = new SelectableNavigation
                {
                    Mode = NavigationMode.Explicit,
                    Up = i > 0 ? items[i - 1] : null,
                    Down = i + 1 < items.Count ? items[i + 1] : null,
                };
            }

            list.SetActive(true);
            if (value < items.Count)
            {
                var selected = items[value];
                EventSystem.current?.SetSelectedGameObject(selected.gameObject);
                var scrollView = list.GetComponentInChildren<YauiScrollView>();
                if (scrollView != null)
                {
                    Tickers.Add(new ScrollWhenLaidOut(scrollView, selected.Element));
                }
            }
        }

        /// <summary>Hides the list.</summary>
        public void Hide()
        {
            items.Clear();
            DestroyObject(list);
            DestroyObject(blocker);
            list = null;
            blocker = null;
        }

        void OnItemSelected(int index)
        {
            Value = index;
            Hide();
            Select();
        }

        static void DestroyObject(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                // Inactive right away: destroying waits for the end of the frame.
                go.SetActive(false);
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        /// <summary>The component at the same place in a copy of a hierarchy.</summary>
        static T Corresponding<T>(T original, Transform originalRoot, Transform copyRoot) where T : Component
        {
            var path = new List<int>();
            for (var t = original.transform; t != originalRoot; t = t.parent)
            {
                if (t == null)
                {
                    return null;
                }

                path.Add(t.GetSiblingIndex());
            }

            var current = copyRoot;
            for (var i = path.Count - 1; i >= 0; i--)
            {
                if (path[i] >= current.childCount)
                {
                    return null;
                }

                current = current.GetChild(path[i]);
            }

            return current.GetComponent<T>();
        }

        /// <summary>Scrolls the list to the selected item once it is laid out.</summary>
        sealed class ScrollWhenLaidOut : ITicker
        {
            readonly YauiScrollView view;
            readonly YauiElement target;
            int frames;

            public ScrollWhenLaidOut(YauiScrollView view, YauiElement target)
            {
                this.view = view;
                this.target = target;
            }

            public bool Tick(float time)
            {
                if (view == null || target == null || ++frames > 10)
                {
                    return false;
                }

                if (target.LayoutRect.height <= 0f)
                {
                    return true;
                }

                view.ScrollIntoView(target);
                return false;
            }
        }
    }
}
