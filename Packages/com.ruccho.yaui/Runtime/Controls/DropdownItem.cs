using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Yaui
{
    /// <summary>An option of an open dropdown list: cancelling on it closes the list.</summary>
    internal sealed class DropdownItem : MonoBehaviour, ICancelHandler
    {
        [NonSerialized] public YauiDropdown Dropdown;

        public void OnCancel(BaseEventData eventData)
        {
            if (Dropdown != null)
            {
                Dropdown.Hide();
                Dropdown.Select();
            }
        }
    }
}