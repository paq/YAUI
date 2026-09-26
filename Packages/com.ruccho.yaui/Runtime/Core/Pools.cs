using System.Collections.Generic;
using Yaui.Layout.Yoga;
using Yaui.Text;

namespace Yaui.Core
{
    /// <summary>
    /// Reuses the objects of destroyed elements: Yoga node records, and text generators (which hold native
    /// generation info). Nodes are returned at submission, when no layout runs on the tree.
    /// </summary>
    internal static class Pools
    {
        private static readonly List<YogaNode> Released = new();
        private static readonly Stack<AtgText> Texts = new();

        public static YogaNode RentNode()
        {
            return YogaNodeStore.Create();
        }

        /// <summary>Any time on the main thread: the node goes back to the pool at the next submission.</summary>
        public static void ReleaseNode(YogaNode node)
        {
            Released.Add(node);
        }

        /// <summary>Main thread, at submission (no layout in flight): detaches and resets the released nodes.</summary>
        public static void RecycleReleasedNodes()
        {
            foreach (var node in Released) YogaNodeStore.Destroy(node);

            Released.Clear();
        }

        public static AtgText RentText()
        {
            return Texts.Count > 0 ? Texts.Pop() : new AtgText();
        }

        /// <summary>Main thread: disposes the pooled text generators (their caches are stale).</summary>
        public static void ClearTexts()
        {
            while (Texts.Count > 0) Texts.Pop().Dispose();
        }

        /// <summary>Main thread: the text must not be generating.</summary>
        public static void ReturnText(AtgText text)
        {
            Texts.Push(text);
        }
    }
}