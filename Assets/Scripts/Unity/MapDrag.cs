using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Dynamons
{
    public sealed class MapDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Frame;
        public Action<float> Move;
        private float previous;
        private int pointer;
        private bool dragging;
        public void OnBeginDrag(PointerEventData e)
        {
            if (dragging || e.button != PointerEventData.InputButton.Left) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Frame, e.pressPosition, e.pressEventCamera, out var p))
            { dragging = true; pointer = e.pointerId; previous = p.x; }
        }
        public void OnDrag(PointerEventData e)
        {
            if (!dragging || e.pointerId != pointer) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(Frame, e.position, e.pressEventCamera, out var p))
            { Move?.Invoke(p.x - previous); previous = p.x; }
        }
        public void OnEndDrag(PointerEventData e) { if (pointer == e.pointerId) dragging = false; }
        private void OnDisable() { dragging = false; }
    }
}
