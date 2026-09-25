using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>A selectable that invokes <see cref="OnClick"/> when clicked or submitted.</summary>
    [AddComponentMenu("YAUI/Button")]
    public class YauiButton : YauiSelectable, IPointerClickHandler, ISubmitHandler
    {
        [SerializeField] UnityEvent onClick = new();

        public UnityEvent OnClick
        {
            get => onClick;
            set => onClick = value ?? new UnityEvent();
        }

        public virtual void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                Press();
            }
        }

        public virtual void OnSubmit(BaseEventData eventData)
        {
            Press();
            FlashPressed();
        }

        void Press()
        {
            if (IsInteractable)
            {
                onClick.Invoke();
            }
        }
    }
}
