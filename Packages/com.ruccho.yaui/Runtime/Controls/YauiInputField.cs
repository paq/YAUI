using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Yaui.Text;

namespace Yaui
{
    public enum InputLineType
    {
        SingleLine,

        /// <summary>Wraps into lines; Enter submits.</summary>
        MultiLineSubmit,

        /// <summary>Wraps into lines; Enter starts a new line.</summary>
        MultiLineNewline,
    }

    public enum InputContentType
    {
        Standard,
        IntegerNumber,
        DecimalNumber,
        Alphanumeric,

        /// <summary>Shows each character as an asterisk and does not copy.</summary>
        Password,
    }

    /// <summary>
    /// An editable text: a selectable that shows its text in a <see cref="YauiText"/>, with a caret and a
    /// selection, keyboard editing (with the clipboard), IME composition and the on-screen keyboard of mobile
    /// platforms. Selecting it starts editing; Enter (single line), Escape or deselecting ends it.
    /// </summary>
    /// <remarks>
    /// The caret and the selection are elements created next to the text (not saved), in its parent: make the
    /// parent clip its children (<see cref="YauiElement.ClipChildren"/>) so that long texts scroll inside it.
    /// Caret positions come from ATG's text selection service; without it the field still edits, without a caret.
    /// </remarks>
    [AddComponentMenu("YAUI/Input Field")]
    public class YauiInputField : YauiSelectable, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler, IUpdateSelectedHandler, ISubmitHandler
    {
        [Serializable]
        public class InputEvent : UnityEvent<string>
        {
        }

        [SerializeField] YauiText textComponent;

        /// <summary>Shown while the text is empty. Its opacity is driven by the input field.</summary>
        [SerializeField] YauiElement placeholder;

        [SerializeField] [TextArea(1, 6)] string text = "";

        /// <summary>The maximum number of characters, or 0 for no limit.</summary>
        [SerializeField] int characterLimit;

        [SerializeField] InputLineType lineType = InputLineType.SingleLine;
        [SerializeField] InputContentType contentType = InputContentType.Standard;
        [SerializeField] TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;
        [SerializeField] bool readOnly;
        [SerializeField] bool selectAllOnFocus = true;
        [SerializeField] Color caretColor = new(0.2f, 0.2f, 0.2f, 1f);
        [SerializeField] float caretWidth = 2f;

        /// <summary>Blinks per second, or 0 for a steady caret.</summary>
        [SerializeField] float caretBlinkRate = 0.85f;

        [SerializeField] Color selectionColor = new(0.66f, 0.81f, 1f, 0.75f);
        [SerializeField] InputEvent onValueChanged = new();

        /// <summary>Invoked when editing ends, for any reason.</summary>
        [SerializeField] InputEvent onEndEdit = new();

        /// <summary>Invoked when Enter (or Done on the on-screen keyboard) ends editing.</summary>
        [SerializeField] InputEvent onSubmit = new();

        static readonly Event ProcessingEvent = new();

        [NonSerialized] bool focused;
        [NonSerialized] int caret;
        [NonSerialized] int anchor;
        [NonSerialized] string composition = "";
        [NonSerialized] string original;
        [NonSerialized] float blinkStart;
        [NonSerialized] Vector2 scroll;
        [NonSerialized] TouchScreenKeyboard keyboard;
        [NonSerialized] YauiText subscribedText;
        [NonSerialized] YauiElement caretElement;
        [NonSerialized] readonly List<YauiElement> selectionBoxes = new();
        [NonSerialized] bool dragging;

        #region Properties

        public string Text
        {
            get => text;
            set => SetText(value, true);
        }

        public void SetTextWithoutNotify(string value) => SetText(value, false);

        public YauiText TextComponent
        {
            get => textComponent;
            set
            {
                textComponent = value;
                UpdateDisplay();
            }
        }

        public YauiElement Placeholder
        {
            get => placeholder;
            set
            {
                placeholder = value;
                UpdateDisplay();
            }
        }

        public int CharacterLimit
        {
            get => characterLimit;
            set
            {
                characterLimit = Mathf.Max(value, 0);
                if (characterLimit > 0 && text.Length > characterLimit)
                {
                    Text = text.Substring(0, characterLimit);
                }
            }
        }

        public InputLineType LineType
        {
            get => lineType;
            set
            {
                lineType = value;
                UpdateDisplay();
            }
        }

        public InputContentType ContentType
        {
            get => contentType;
            set
            {
                contentType = value;
                UpdateDisplay();
            }
        }

        public TouchScreenKeyboardType KeyboardType
        {
            get => keyboardType;
            set => keyboardType = value;
        }

        public bool ReadOnly
        {
            get => readOnly;
            set => readOnly = value;
        }

        public bool SelectAllOnFocus
        {
            get => selectAllOnFocus;
            set => selectAllOnFocus = value;
        }

        public Color CaretColor
        {
            get => caretColor;
            set
            {
                caretColor = value;
                if (caretElement != null)
                {
                    caretElement.BackgroundColor = value;
                }
            }
        }

        public float CaretWidth
        {
            get => caretWidth;
            set => caretWidth = value;
        }

        public float CaretBlinkRate
        {
            get => caretBlinkRate;
            set => caretBlinkRate = value;
        }

        public Color SelectionColor
        {
            get => selectionColor;
            set
            {
                selectionColor = value;
                foreach (var box in selectionBoxes)
                {
                    box.BackgroundColor = value;
                }
            }
        }

        public InputEvent OnValueChanged
        {
            get => onValueChanged;
            set => onValueChanged = value ?? new InputEvent();
        }

        public InputEvent OnEndEdit
        {
            get => onEndEdit;
            set => onEndEdit = value ?? new InputEvent();
        }

        public InputEvent OnSubmitText
        {
            get => onSubmit;
            set => onSubmit = value ?? new InputEvent();
        }

        /// <summary>Whether the field is being edited.</summary>
        public bool IsFocused => focused;

        bool IsMultiLine => lineType != InputLineType.SingleLine;

        /// <summary>The caret, as an index into <see cref="Text"/>.</summary>
        public int CaretPosition
        {
            get => caret;
            set
            {
                caret = anchor = Mathf.Clamp(value, 0, text.Length);
                OnCaretMoved();
            }
        }

        /// <summary>The other end of the selection (the caret if nothing is selected).</summary>
        public int SelectionAnchor
        {
            get => anchor;
            set
            {
                anchor = Mathf.Clamp(value, 0, text.Length);
                OnCaretMoved();
            }
        }

        bool HasSelection => caret != anchor;

        int SelectionStart => Mathf.Min(caret, anchor);

        int SelectionEnd => Mathf.Max(caret, anchor);

        /// <summary>The selected text, or empty.</summary>
        public string SelectedText => HasSelection ? text.Substring(SelectionStart, SelectionEnd - SelectionStart) : "";

        #endregion

        #region Lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();
            text ??= "";
            UpdateDisplay();
        }

        protected override void OnDisable()
        {
            Deactivate(false);
            DestroyVisuals();
            base.OnDisable();
        }

        protected virtual void OnDestroy() => DestroyVisuals();

        protected override void OnValidate()
        {
            base.OnValidate();
            text ??= "";
            characterLimit = Mathf.Max(characterLimit, 0);
            if (isActiveAndEnabled)
            {
                UpdateDisplay();
            }
        }

        /// <summary>Starts editing, if the field is interactable.</summary>
        public void ActivateInputField()
        {
            if (focused || !IsInteractable || textComponent == null)
            {
                return;
            }

            focused = true;
            original = text;
            composition = "";
            caret = text.Length;
            anchor = selectAllOnFocus ? 0 : caret;
            scroll = Vector2.zero;
            blinkStart = Time.unscaledTime;
            subscribedText = textComponent;
            subscribedText.Rendered += UpdateVisuals;

            if (TouchScreenKeyboard.isSupported && Application.isMobilePlatform && !readOnly)
            {
                keyboard = TouchScreenKeyboard.Open(text, keyboardType,
                    contentType == InputContentType.Standard, IsMultiLine,
                    contentType == InputContentType.Password, false, "", characterLimit);
                if (keyboard != null && keyboard.canSetSelection)
                {
                    keyboard.selection = new RangeInt(anchor, caret - anchor);
                }
            }
            else
            {
                Ime.Enable(this);
            }

            UpdateDisplay();
            UpdateVisuals();
        }

        /// <summary>Ends editing and invokes <see cref="OnEndEdit"/>.</summary>
        public void DeactivateInputField() => Deactivate(true);

        void Deactivate(bool notify)
        {
            if (!focused)
            {
                return;
            }

            focused = false;
            dragging = false;
            composition = "";
            if (keyboard != null)
            {
                keyboard.active = false;
                keyboard = null;
            }

            Ime.Disable(this);
            if (subscribedText != null)
            {
                subscribedText.Rendered -= UpdateVisuals;
                subscribedText = null;
            }

            caret = anchor = Mathf.Clamp(caret, 0, text.Length);
            UpdateDisplay();
            HideVisuals();
            if (notify)
            {
                onEndEdit.Invoke(text);
            }
        }

        public void SelectAll()
        {
            anchor = 0;
            caret = text.Length;
            OnCaretMoved();
        }

        #endregion

        #region Events

        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            ActivateInputField();
        }

        public override void OnDeselect(BaseEventData eventData)
        {
            Deactivate(true);
            base.OnDeselect(eventData);
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            // Selected but not editing (after Enter or Escape): submitting edits again.
            if (!focused)
            {
                ActivateInputField();
            }
        }

        public override void OnMove(AxisEventData eventData)
        {
            // The arrow keys move the caret while editing.
            if (!focused)
            {
                base.OnMove(eventData);
            }
        }

        public override void OnPointerDown(PointerEventData eventData)
        {
            var wasFocused = focused;
            base.OnPointerDown(eventData);
            if (eventData.button != PointerEventData.InputButton.Left || !IsInteractable)
            {
                return;
            }

            if (!focused)
            {
                ActivateInputField();
            }

            if (!focused || keyboard != null)
            {
                return;
            }

            CommitComposition();
            if (!TryIndexAt(eventData.position, out var index))
            {
                return;
            }

            // The first press selects everything (if configured); later ones place the caret.
            if (!wasFocused && selectAllOnFocus && eventData.clickCount <= 1)
            {
                return;
            }

            if (eventData.clickCount == 2)
            {
                SelectWordAt(index);
            }
            else if (eventData.clickCount >= 3)
            {
                SelectAll();
            }
            else
            {
                caret = anchor = index;
                OnCaretMoved();
            }
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
        }

        public virtual void OnBeginDrag(PointerEventData eventData)
        {
            dragging = focused && keyboard == null && eventData.button == PointerEventData.InputButton.Left;
        }

        public virtual void OnDrag(PointerEventData eventData)
        {
            if (dragging && TryIndexAt(eventData.position, out var index) && index != caret)
            {
                caret = index;
                OnCaretMoved();
            }
        }

        public virtual void OnEndDrag(PointerEventData eventData) => dragging = false;

        /// <summary>Sent every frame to the selected object: the keyboard and the IME.</summary>
        public virtual void OnUpdateSelected(BaseEventData eventData)
        {
            if (!focused)
            {
                return;
            }

            if (keyboard != null)
            {
                UpdateTouchKeyboard();
            }
            else
            {
                while (Event.PopEvent(ProcessingEvent))
                {
                    var type = ProcessingEvent.rawType;
                    if (type == EventType.KeyDown && !KeyPressed(ProcessingEvent))
                    {
                        break;
                    }

                    if (type is EventType.ValidateCommand or EventType.ExecuteCommand)
                    {
                        ExecuteCommand(ProcessingEvent.commandName);
                    }
                }

                if (focused)
                {
                    Ime.Update(this);
                }
            }

            UpdateBlink();

            // Editing keeps the navigation (arrow keys, submit) from leaving the field.
            eventData.Use();
        }

        #endregion

        #region Editing

        void SetText(string value, bool notify)
        {
            value ??= "";
            if (characterLimit > 0 && value.Length > characterLimit)
            {
                value = value.Substring(0, characterLimit);
            }

            if (value == text)
            {
                return;
            }

            text = value;
            caret = Mathf.Clamp(caret, 0, text.Length);
            anchor = Mathf.Clamp(anchor, 0, text.Length);
            if (keyboard != null && keyboard.text != text)
            {
                keyboard.text = text;
            }

            UpdateDisplay();
            if (notify)
            {
                onValueChanged.Invoke(text);
            }
        }

        /// <summary>Handles a key press as if it came from the keyboard (tests). Returns false when editing ended.</summary>
        internal bool ProcessKey(Event e) => focused && KeyPressed(e);

        /// <summary>The caret element while editing (tests).</summary>
        internal YauiElement CaretElement => caretElement;

        /// <summary>The selection boxes in use (tests).</summary>
        internal int VisibleSelectionBoxes
        {
            get
            {
                var count = 0;
                foreach (var box in selectionBoxes)
                {
                    count += box.RenderTransform.Scale.x > 0f ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>Returns false when editing ended.</summary>
        bool KeyPressed(Event e)
        {
            var mac = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX;
            var ctrl = mac ? e.command : e.control;
            var word = mac ? e.alt : e.control;
            var shift = e.shift;
            switch (e.keyCode)
            {
                case KeyCode.Backspace:
                    if (!readOnly)
                    {
                        Delete(word ? PreviousWord(caret) : PreviousCharacter(caret), caret);
                    }

                    return true;
                case KeyCode.Delete:
                    if (!readOnly)
                    {
                        Delete(caret, word ? NextWord(caret) : NextCharacter(caret));
                    }

                    return true;
                case KeyCode.LeftArrow:
                    Move(word ? PreviousWord(caret) : HasSelection && !shift ? SelectionStart : PreviousCharacter(caret),
                        shift);
                    return true;
                case KeyCode.RightArrow:
                    Move(word ? NextWord(caret) : HasSelection && !shift ? SelectionEnd : NextCharacter(caret), shift);
                    return true;
                case KeyCode.UpArrow:
                    Move(IsMultiLine ? LineAbove(caret) : 0, shift);
                    return true;
                case KeyCode.DownArrow:
                    Move(IsMultiLine ? LineBelow(caret) : text.Length, shift);
                    return true;
                case KeyCode.Home:
                    Move(ctrl || !IsMultiLine ? 0 : LineStart(caret), shift);
                    return true;
                case KeyCode.End:
                    Move(ctrl || !IsMultiLine ? text.Length : LineEnd(caret), shift);
                    return true;
                case KeyCode.A when ctrl:
                    SelectAll();
                    return true;
                case KeyCode.C when ctrl:
                    ExecuteCommand("Copy");
                    return true;
                case KeyCode.V when ctrl:
                    ExecuteCommand("Paste");
                    return true;
                case KeyCode.X when ctrl:
                    ExecuteCommand("Cut");
                    return true;
                case KeyCode.Escape:
                    SetText(original, true);
                    Deactivate(true);
                    return false;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (lineType != InputLineType.MultiLineNewline)
                    {
                        onSubmit.Invoke(text);
                        Deactivate(true);
                        return false;
                    }

                    break;
            }

            var c = e.character;
            if (c == '\0' || ctrl && !e.alt || readOnly)
            {
                return true;
            }

            if (c is '\r' or (char)3)
            {
                c = '\n';
            }

            if (!IsMultiLine && c is '\t' or '\n' || lineType == InputLineType.MultiLineSubmit && c == '\n')
            {
                return true;
            }

            if (IsValid(c))
            {
                Insert(c.ToString());
            }

            return true;
        }

        void ExecuteCommand(string command)
        {
            switch (command)
            {
                case "Copy":
                    if (contentType != InputContentType.Password && HasSelection)
                    {
                        GUIUtility.systemCopyBuffer = SelectedText;
                    }

                    break;
                case "Cut":
                    if (contentType != InputContentType.Password && HasSelection && !readOnly)
                    {
                        GUIUtility.systemCopyBuffer = SelectedText;
                        Delete(caret, caret);
                    }

                    break;
                case "Paste":
                    if (!readOnly)
                    {
                        var pasted = GUIUtility.systemCopyBuffer ?? "";
                        var filtered = new System.Text.StringBuilder(pasted.Length);
                        foreach (var ch in pasted)
                        {
                            var c = ch == '\r' ? '\n' : ch;
                            if ((c != '\n' || lineType == InputLineType.MultiLineNewline) && IsValid(c))
                            {
                                filtered.Append(c);
                            }
                        }

                        Insert(filtered.ToString());
                    }

                    break;
                case "SelectAll":
                    SelectAll();
                    break;
            }
        }

        bool IsValid(char c)
        {
            if (c < ' ' && c is not ('\n' or '\t'))
            {
                return false;
            }

            var at = SelectionStart;
            switch (contentType)
            {
                case InputContentType.IntegerNumber:
                    return char.IsDigit(c) || c == '-' && at == 0 && !text.Contains('-');
                case InputContentType.DecimalNumber:
                    return char.IsDigit(c) || c == '-' && at == 0 && !text.Contains('-') ||
                           c == '.' && !text.Contains('.');
                case InputContentType.Alphanumeric:
                    return char.IsLetterOrDigit(c);
                default:
                    return true;
            }
        }

        /// <summary>Replaces the selection with <paramref name="value"/> (within the character limit).</summary>
        void Insert(string value)
        {
            if (readOnly)
            {
                return;
            }

            var start = SelectionStart;
            var removed = text.Remove(start, SelectionEnd - start);
            if (characterLimit > 0)
            {
                var room = characterLimit - removed.Length;
                if (room <= 0)
                {
                    return;
                }

                if (value.Length > room)
                {
                    value = value.Substring(0, room);
                }
            }

            caret = anchor = start + value.Length;
            SetText(removed.Insert(start, value), true);
            OnCaretMoved();
        }

        /// <summary>Deletes the selection, or the range if nothing is selected.</summary>
        void Delete(int from, int to)
        {
            if (HasSelection)
            {
                from = SelectionStart;
                to = SelectionEnd;
            }

            from = Mathf.Clamp(Mathf.Min(from, to), 0, text.Length);
            to = Mathf.Clamp(Mathf.Max(from, to), 0, text.Length);
            if (from == to)
            {
                return;
            }

            caret = anchor = from;
            SetText(text.Remove(from, to - from), true);
            OnCaretMoved();
        }

        void Move(int index, bool extend)
        {
            caret = Mathf.Clamp(index, 0, text.Length);
            if (!extend)
            {
                anchor = caret;
            }

            OnCaretMoved();
        }

        int PreviousCharacter(int index)
        {
            if (index <= 0)
            {
                return 0;
            }

            index--;
            return index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])
                ? index - 1
                : index;
        }

        int NextCharacter(int index)
        {
            if (index >= text.Length)
            {
                return text.Length;
            }

            index++;
            return index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])
                ? index + 1
                : index;
        }

        // Word and line queries run on the shown text, which has the indices of the text while nothing is composed.
        bool CanQuery => composition.Length == 0 && textComponent != null && textComponent.SupportsSelection &&
                         textComponent.RenderedLength == text.Length;

        int PreviousWord(int index) =>
            CanQuery ? Mathf.Clamp(AtgSelection.EndOfPreviousWord(textComponent.SelectionInfo, index), 0, index)
                : PreviousCharacter(index);

        int NextWord(int index) =>
            CanQuery ? Mathf.Clamp(AtgSelection.StartOfNextWord(textComponent.SelectionInfo, index), index, text.Length)
                : NextCharacter(index);

        int LineStart(int index)
        {
            var start = text.LastIndexOf('\n', Mathf.Max(index - 1, 0));
            var paragraph = index > 0 && start >= 0 ? start + 1 : 0;
            if (!CanQuery)
            {
                return paragraph;
            }

            // The start of the wrapped line: the caret at the left of the line.
            textComponent.GetCaret(index, out var top, out var height);
            return Mathf.Max(paragraph, textComponent.IndexAt(new Vector2(-1e5f, top.y + height * 0.5f)));
        }

        int LineEnd(int index)
        {
            var end = text.IndexOf('\n', index);
            var paragraph = end >= 0 ? end : text.Length;
            if (!CanQuery)
            {
                return paragraph;
            }

            textComponent.GetCaret(index, out var top, out var height);
            return Mathf.Min(paragraph, textComponent.IndexAt(new Vector2(1e5f, top.y + height * 0.5f)));
        }

        int LineAbove(int index)
        {
            if (!CanQuery)
            {
                return 0;
            }

            textComponent.GetCaret(index, out var top, out _);
            return top.y <= 0.5f ? 0 : textComponent.IndexAt(new Vector2(top.x, top.y - 1f));
        }

        int LineBelow(int index)
        {
            if (!CanQuery)
            {
                return text.Length;
            }

            textComponent.GetCaret(index, out var top, out var height);
            var below = textComponent.IndexAt(new Vector2(top.x, top.y + height + 1f));
            return below <= index && top.y + height >= textComponent.LayoutRect.height - 0.5f ? text.Length : below;
        }

        void SelectWordAt(int index)
        {
            if (!CanQuery)
            {
                SelectAll();
                return;
            }

            AtgSelection.WordBounds(textComponent.SelectionInfo, Mathf.Min(index, Mathf.Max(text.Length - 1, 0)),
                out var start, out var end);
            if (start < 0)
            {
                start = index;
            }

            anchor = Mathf.Clamp(start, 0, text.Length);
            caret = Mathf.Clamp(end, anchor, text.Length);
            OnCaretMoved();
        }

        bool TryIndexAt(Vector2 screenPosition, out int index)
        {
            index = 0;
            if (!CanQuery || !textComponent.ScreenToLocal(screenPosition, out var local))
            {
                return false;
            }

            index = Mathf.Clamp(textComponent.IndexAt(local), 0, text.Length);
            return true;
        }

        /// <summary>The IME composition changed: shown inline at the caret, replacing the selection.</summary>
        internal void SetComposition(string value)
        {
            value ??= "";
            if (!focused || readOnly || value == composition)
            {
                return;
            }

            if (value.Length > 0 && HasSelection)
            {
                Delete(caret, caret);
            }

            composition = value;
            UpdateDisplay();
            OnCaretMoved();
        }

        /// <summary>Drops the composition (the IME commits it as characters).</summary>
        void CommitComposition()
        {
            if (composition.Length > 0)
            {
                composition = "";
                UpdateDisplay();
            }
        }

        void UpdateTouchKeyboard()
        {
            switch (keyboard.status)
            {
                case TouchScreenKeyboard.Status.Visible:
                    if (keyboard.text != text)
                    {
                        // The keyboard edits freely: keep what the content type allows.
                        var filtered = new System.Text.StringBuilder();
                        foreach (var c in keyboard.text)
                        {
                            if (c >= ' ' || c == '\n' && IsMultiLine || c == '\t')
                            {
                                filtered.Append(c);
                            }
                        }

                        caret = anchor = filtered.Length;
                        SetText(filtered.ToString(), true);
                    }

                    if (keyboard.canGetSelection)
                    {
                        var selection = keyboard.selection;
                        anchor = Mathf.Clamp(selection.start, 0, text.Length);
                        caret = Mathf.Clamp(selection.end, 0, text.Length);
                    }

                    OnCaretMoved();
                    break;
                case TouchScreenKeyboard.Status.Done:
                    onSubmit.Invoke(text);
                    Deactivate(true);
                    break;
                case TouchScreenKeyboard.Status.Canceled:
                    SetText(original, true);
                    Deactivate(true);
                    break;
                case TouchScreenKeyboard.Status.LostFocus:
                    Deactivate(true);
                    break;
            }
        }

        #endregion

        #region Display

        /// <summary>The text shown: masked for passwords, with the composition at the caret.</summary>
        string DisplayText()
        {
            var shown = contentType == InputContentType.Password ? new string('*', text.Length) : text;
            if (composition.Length > 0)
            {
                var composed = contentType == InputContentType.Password ? new string('*', composition.Length) : composition;
                shown = shown.Insert(Mathf.Clamp(caret, 0, shown.Length), composed);
            }

            return shown;
        }

        /// <summary>The caret in the shown text.</summary>
        int DisplayCaret => caret + composition.Length;

        void UpdateDisplay()
        {
            if (textComponent != null)
            {
                // Tags would move the indices of the characters.
                if (textComponent.RichText)
                {
                    textComponent.RichText = false;
                }

                if (!IsMultiLine && textComponent.WordWrap)
                {
                    textComponent.WordWrap = false;
                }

                if (textComponent.Overflow != TextOverflow.Visible)
                {
                    textComponent.Overflow = TextOverflow.Visible;
                }

                textComponent.Text = DisplayText();
            }

            if (placeholder != null)
            {
                var empty = text.Length == 0 && composition.Length == 0;
                var opacity = empty ? 1f : 0f;
                if (placeholder.Opacity != opacity)
                {
                    placeholder.Opacity = opacity;
                }
            }
        }

        void OnCaretMoved()
        {
            blinkStart = Time.unscaledTime;
            if (keyboard != null && keyboard.canSetSelection)
            {
                var selection = new RangeInt(SelectionStart, SelectionEnd - SelectionStart);
                if (!keyboard.selection.Equals(selection))
                {
                    keyboard.selection = selection;
                }
            }

            UpdateVisuals();
        }

        /// <summary>The text area: the text's parent, where the caret and the selection are.</summary>
        YauiElement Area => textComponent != null ? Track.ParentOf(textComponent) : null;

        /// <summary>
        /// Places the caret and the selection from the last generation of the text, and scrolls the text to keep
        /// the caret inside the area. Runs again after each new generation of the text.
        /// </summary>
        void UpdateVisuals()
        {
            var area = Area;
            if (!focused || area == null || !textComponent.SupportsSelection)
            {
                HideVisuals();
                return;
            }

            // Until the text is generated again after an edit, the visuals stay; they follow at its rendering.
            if (textComponent.RenderedLength != DisplayText().Length)
            {
                return;
            }

            EnsureVisuals(area);
            var textOffset = textComponent.LayoutRect.position;
            var bounds = area.ContentBox;

            // Scrolls so that the caret is inside the area, and not further than the text needs: the text's box
            // may be narrower than its glyphs (a single line in a flex container), so its end is the end caret.
            textComponent.GetCaret(DisplayCaret, out var top, out var height);
            var caretPosition = textOffset + top;
            textComponent.GetCaret(DisplayText().Length, out var endTop, out var endHeight);
            var extent = Vector2.Max(textComponent.LayoutRect.size,
                new Vector2(endTop.x + caretWidth, endTop.y + endHeight));
            for (var axis = 0; axis < 2; axis++)
            {
                if (axis == 1 && !IsMultiLine)
                {
                    scroll.y = 0f;
                    continue;
                }

                var size = axis == 0 ? caretWidth : height;
                var min = bounds.min[axis];
                var max = bounds.max[axis];
                var position = caretPosition[axis] - scroll[axis];
                if (position + size > max)
                {
                    scroll[axis] += position + size - max;
                }

                if (position < min)
                {
                    scroll[axis] -= min - position;
                }

                scroll[axis] = Mathf.Clamp(scroll[axis], 0f, Mathf.Max(textOffset[axis] + extent[axis] - max, 0f));
            }

            if (textComponent.Translate != -scroll)
            {
                textComponent.Translate = -scroll;
            }

            // The visuals are absolutely positioned at the area's padding box; they move and scale by their
            // render transforms, so that they never lay out again.
            var origin = new Vector2(area.Box.BorderWidth, area.Box.BorderWidth);
            Place(caretElement, caretPosition - scroll - origin, new Vector2(caretWidth, height));

            var rects = HasSelection && composition.Length == 0
                ? textComponent.SelectionRects(SelectionStart, SelectionEnd)
                : Array.Empty<Rect>();
            while (selectionBoxes.Count < rects.Length)
            {
                selectionBoxes.Add(CreateVisual(area, "Selection", selectionColor, true));
            }

            for (var i = 0; i < selectionBoxes.Count; i++)
            {
                if (i < rects.Length)
                {
                    Place(selectionBoxes[i], textOffset + rects[i].position - scroll - origin, rects[i].size);
                }
                else
                {
                    Place(selectionBoxes[i], Vector2.zero, Vector2.zero);
                }
            }

            UpdateBlink();

            // The IME's candidate window follows the bottom of the caret. The text's transform already scrolls it.
            if (textComponent.LocalToScreen(top + new Vector2(0f, height), out var screen))
            {
                Ime.SetCursor(this, screen);
            }
        }

        static void Place(YauiElement element, Vector2 position, Vector2 size)
        {
            var transform = new TransformStyle
            {
                Translate = position,
                Scale = size,
                Pivot = Vector2.zero,
            };

            if (!element.RenderTransform.Equals(transform))
            {
                element.RenderTransform = transform;
            }
        }

        void UpdateBlink()
        {
            if (caretElement == null)
            {
                return;
            }

            var visible = focused && !HasSelection &&
                          (caretBlinkRate <= 0f || (Time.unscaledTime - blinkStart) * caretBlinkRate % 1f < 0.5f);
            var opacity = visible ? 1f : 0f;
            if (caretElement.Opacity != opacity)
            {
                caretElement.Opacity = opacity;
            }
        }

        void EnsureVisuals(YauiElement area)
        {
            if (caretElement == null || caretElement.transform.parent != area.transform)
            {
                DestroyVisuals();
                caretElement = CreateVisual(area, "Caret", caretColor, false);
            }
        }

        /// <summary>A 1 x 1 box at the area's origin, scaled into place; the selection below the text, the caret above.</summary>
        YauiElement CreateVisual(YauiElement area, string name, Color color, bool belowText)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(area.transform, false);
            var index = textComponent.transform.GetSiblingIndex();
            go.transform.SetSiblingIndex(belowText ? index : index + 1);
            var element = go.AddComponent<YauiElement>();
            var layout = element.Layout;
            layout.Position = PositionType.Absolute;
            layout.Inset = new Edges(Length.Points(0f), Length.Points(0f), Length.Auto, Length.Auto);
            layout.Width = 1f;
            layout.Height = 1f;
            element.Layout = layout;
            var box = element.Box;
            box.BackgroundColor = color;
            element.Box = box;
            element.RaycastTarget = false;
            Place(element, Vector2.zero, Vector2.zero);
            return element;
        }

        void HideVisuals()
        {
            if (caretElement != null)
            {
                Place(caretElement, Vector2.zero, Vector2.zero);
            }

            foreach (var box in selectionBoxes)
            {
                if (box != null)
                {
                    Place(box, Vector2.zero, Vector2.zero);
                }
            }
        }

        void DestroyVisuals()
        {
            if (caretElement != null)
            {
                DestroyObject(caretElement.gameObject);
            }

            foreach (var box in selectionBoxes)
            {
                if (box != null)
                {
                    DestroyObject(box.gameObject);
                }
            }

            caretElement = null;
            selectionBoxes.Clear();
        }

        static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
            }
        }

        #endregion

        /// <summary>
        /// The IME of the platform: composition and the position of its candidate window, through the IME members of
        /// <see cref="Input"/> like uGUI's input fields. They work with the Input System alone too; its keyboard's
        /// IME command does not turn on the IME the engine uses.
        /// </summary>
        static class Ime
        {
            static YauiInputField owner;

            public static void Enable(YauiInputField field)
            {
                owner = field;
                Input.imeCompositionMode = IMECompositionMode.On;
            }

            public static void Disable(YauiInputField field)
            {
                if (owner != field)
                {
                    return;
                }

                owner = null;
                Input.imeCompositionMode = IMECompositionMode.Auto;
            }

            public static void Update(YauiInputField field)
            {
                if (owner == field)
                {
                    field.SetComposition(Input.compositionString);
                }
            }

            /// <summary>Screen positions have their origin at the bottom-left; the IME's at the top-left.</summary>
            public static void SetCursor(YauiInputField field, Vector2 screenPosition)
            {
                if (owner == field)
                {
                    Input.compositionCursorPos = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
                }
            }
        }
    }
}
