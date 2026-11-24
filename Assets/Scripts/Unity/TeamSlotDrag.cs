using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Dynamons
{
    public sealed class TeamSlotDrag : MonoBehaviour, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public RectTransform Frame;
        public int RosterIndex;
        public bool IsParty;
        public Action<int> OnTap;
        public Action<int, bool, Vector2> OnStartDrag;
        public Action<Vector2> OnDragMove;
        public Action<Vector2> OnFinishDrag;
        public Action OnCancelDrag;

        private Vector2 downLogical;
        private int pointerId;
        private bool pointerDown, dragging;

        private bool TryLogical(PointerEventData e, out Vector2 logical)
        {
            if (Frame != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(Frame, e.position, e.pressEventCamera, out var local))
            {
                logical = new Vector2(
                    Mathf.Clamp(local.x + GameUi.Width * 0.5f, 0f, GameUi.Width),
                    Mathf.Clamp(GameUi.Height * 0.5f - local.y, 0f, GameUi.Height));
                return true;
            }
            logical = Vector2.zero;
            return false;
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (!TryLogical(e, out var pos)) return;
            pointerDown = true;
            dragging = false;
            pointerId = e.pointerId;
            downLogical = pos;
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (!pointerDown || e.pointerId != pointerId || e.button != PointerEventData.InputButton.Left) return;
            if (!TryLogical(e, out var pos)) return;
            if (!dragging && Vector2.Distance(downLogical, pos) > 6f)
            {
                dragging = true;
                OnStartDrag?.Invoke(RosterIndex, IsParty, pos);
            }
        }

        public void OnDrag(PointerEventData e)
        {
            if (!pointerDown || e.pointerId != pointerId) return;
            if (!TryLogical(e, out var pos)) return;
            if (!dragging)
            {
                if (Vector2.Distance(downLogical, pos) <= 6f) return;
                dragging = true;
                OnStartDrag?.Invoke(RosterIndex, IsParty, pos);
            }
            OnDragMove?.Invoke(pos);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!pointerDown || e.pointerId != pointerId) return;
            pointerDown = false;
            if (!dragging) return;
            dragging = false;
            Vector2 pos = TryLogical(e, out var p) ? p : downLogical;
            OnFinishDrag?.Invoke(pos);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (dragging || e.dragging) return;
            if (TryLogical(e, out var pos) && Vector2.Distance(downLogical, pos) > 6f) return;
            OnTap?.Invoke(RosterIndex);
        }

        private void OnDisable()
        {
            bool wasDragging = dragging;
            pointerDown = false;
            dragging = false;
            if (wasDragging) OnCancelDrag?.Invoke();
        }
    }
}
