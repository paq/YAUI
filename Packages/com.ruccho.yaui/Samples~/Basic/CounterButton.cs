using UnityEngine;
using UnityEngine.EventSystems;

namespace Yaui.Samples.Basic
{
    /// <summary>
    /// A button made of a YauiElement: pointer events come from the panel's YauiRaycaster through the EventSystem.
    /// Setters update the element directly; nothing is rebuilt.
    /// </summary>
    [RequireComponent(typeof(YauiElement))]
    public sealed class CounterButton : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] YauiText label;
        [SerializeField] Color normal = new(0.35f, 0.4f, 0.95f);
        [SerializeField] Color hover = new(0.45f, 0.5f, 1f);

        YauiElement element;
        int count;

        void Awake()
        {
            element = GetComponent<YauiElement>();
            element.BackgroundColor = normal;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            count++;
            label.Text = $"Clicked {count} times";
        }

        public void OnPointerEnter(PointerEventData eventData) => element.BackgroundColor = hover;

        public void OnPointerExit(PointerEventData eventData)
        {
            element.BackgroundColor = normal;
            element.Scale = Vector2.one;
        }

        // A render transform animates without relayout.
        public void OnPointerDown(PointerEventData eventData) => element.Scale = new Vector2(0.95f, 0.95f);

        public void OnPointerUp(PointerEventData eventData) => element.Scale = Vector2.one;
    }
}
