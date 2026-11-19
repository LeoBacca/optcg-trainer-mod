using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OPTCGTrainer
{
    // I punti sono relativi all'area di disegno (tutto lo schermo), così il disegno segue la finestra al ridimensionamento.
    public struct InkPoint { public float x, y; }
    public sealed class InkStroke
    {
        public int color, width;
        public List<InkPoint> points = new List<InkPoint>();
    }

    internal sealed class ReplayStrokeGraphic : MaskableGraphic
    {
        internal InkStroke stroke;
        internal static readonly Color32[] Colors = { new Color32(240,70,64,255), new Color32(65,150,255,255),
            new Color32(255,212,34,255), new Color32(64,214,118,255), Color.white, new Color32(24,24,26,255) };
        internal Vector2 Local(InkPoint p) => rectTransform.rect.min + Vector2.Scale(new Vector2(p.x, p.y), rectTransform.rect.size);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (stroke == null || stroke.points.Count == 0) return;
            Color32 tint = Colors[Mathf.Clamp(stroke.color, 0, Colors.Length - 1)];
            float r = rectTransform.rect.height * (0.0018f + Mathf.Clamp(stroke.width, 0, 2) * 0.0018f);
            Vector2 previous = Local(stroke.points[0]);
            foreach (var p in stroke.points)
            {
                var next = Local(p);
                int j = vh.currentVertCount;
                var n = Vector2.Perpendicular((next - previous).normalized) * r;
                vh.AddVert(previous + n, tint, Vector2.zero); vh.AddVert(next + n, tint, Vector2.zero);
                vh.AddVert(next - n, tint, Vector2.zero); vh.AddVert(previous - n, tint, Vector2.zero);
                vh.AddTriangle(j, j+1, j+2); vh.AddTriangle(j, j+2, j+3);
                j = vh.currentVertCount;
                vh.AddVert(next, tint, Vector2.zero);
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4;
                    vh.AddVert(next + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r, tint, Vector2.zero);
                }
                for (int k = 0; k < 8; k++) vh.AddTriangle(j, j + 1 + k, j + 1 + (k + 1) % 8);
                previous = next;
            }
        }
    }

    internal sealed class ReplayInk : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        internal int color, width = 1;
        internal bool erasing, editing;
        readonly List<ReplayStrokeGraphic> graphics = new List<ReplayStrokeGraphic>();
        ReplayStrokeGraphic active;
        RectTransform Area => (RectTransform)transform;
        internal List<InkStroke> Snapshot() => graphics.ConvertAll(g => g.stroke);
        internal void Clear()
        {
            active = null;
            foreach (var g in graphics) Destroy(g.gameObject);
            graphics.Clear();
        }
        internal void Load(List<InkStroke> strokes)
        {
            Clear();
            if (strokes == null) return;
            foreach (var s in strokes) if (s?.points != null && s.points.Count <= 4000)
                Add(new InkStroke { color = s.color, width = s.width, points = new List<InkPoint>(s.points) });
        }
        ReplayStrokeGraphic Add(InkStroke s)
        {
            var go = new GameObject("Marker stroke", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            Look.Place((RectTransform)go.transform, 0, 0, 1, 1);
            var g = go.AddComponent<ReplayStrokeGraphic>();
            g.raycastTarget = false; g.stroke = s; g.SetVerticesDirty(); graphics.Add(g);
            return g;
        }
        public void OnPointerDown(PointerEventData e)
        {
            if (!editing || e.button != PointerEventData.InputButton.Left) return;
            active = null; Sample(e);
        }
        public void OnDrag(PointerEventData e) { if (editing && e.button == PointerEventData.InputButton.Left) Sample(e); }
        public void OnPointerUp(PointerEventData e) { active = null; }
        void Sample(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Area, e.position, e.pressEventCamera, out var local)) return;
            var rect = Area.rect;
            if (rect.width <= 0 || rect.height <= 0 || !rect.Contains(local)) { active = null; return; }
            if (erasing)
            {
                // La gomma toglie il tratto toccato, anche nel mezzo di un segmento veloce.
                for (int i = graphics.Count - 1; i >= 0; i--)
                {
                    var g = graphics[i];
                    var points = g.stroke.points;
                    for (int k = 0; k < points.Count; k++)
                    {
                        var a = g.Local(points[k]); var d = g.Local(points[Mathf.Max(0, k-1)]) - a;
                        var nearest = a + d * Mathf.Clamp01(Vector2.Dot(local - a, d) / Mathf.Max(0.0001f, d.sqrMagnitude));
                        if (Vector2.Distance(local, nearest) > rect.height * 0.015f) continue;
                        graphics.RemoveAt(i); Destroy(g.gameObject); return;
                    }
                }
                return;
            }
            if (active == null || active.stroke.points.Count >= 4000) active = Add(new InkStroke { color = color, width = width });
            var list = active.stroke.points;
            if (list.Count > 0 && (active.Local(list[list.Count-1]) - local).sqrMagnitude < 2f) return;
            list.Add(new InkPoint { x = (local.x - rect.xMin) / rect.width, y = (local.y - rect.yMin) / rect.height });
            active.SetVerticesDirty();
        }
    }
}
