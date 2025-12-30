/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using EasingCore;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing a ScrollRect-style scroll view.
    /// Does not support infinite scrolling or snapping.
    /// Use <see cref="FancyScrollRect{TItemData}"/> if <see cref="FancyScrollView{TItemData, TContext}.Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of <see cref="FancyScrollView{TItemData, TContext}.Context"/>.</typeparam>
    [RequireComponent(typeof(Scroller))]
    public abstract class FancyScrollRect<TItemData, TContext> : FancyScrollView<TItemData, TContext>
        where TContext : class, IFancyScrollRectContext, new()
    {
        /// <summary>
        /// The number of margin cells to keep before recycling.
        /// </summary>
        /// <remarks>
        /// If 0, cells are recycled immediately after going out of view.
        /// If 1 or more, that many cells are kept as margin before recycling.
        /// </remarks>
        [SerializeField] protected float reuseCellMarginCount = 0f;

        /// <summary>
        /// Padding at the head of the content.
        /// </summary>
        [SerializeField] protected float paddingHead = 0f;

        /// <summary>
        /// Padding at the tail of the content.
        /// </summary>
        [SerializeField] protected float paddingTail = 0f;

        /// <summary>
        /// Spacing between cells.
        /// </summary>
        [SerializeField] protected float spacing = 0f;

        /// <summary>
        /// The size of the cell.
        /// </summary>
        protected abstract float CellSize { get; }

        /// <summary>
        /// Whether scrolling is possible.
        /// </summary>
        /// <remarks>
        /// True if the content is larger than the viewport, otherwise false.
        /// </remarks>
        protected virtual bool Scrollable => MaxScrollPosition > 0f;

        private Scroller cachedScroller;

        /// <summary>
        /// The <see cref="FancyScrollView.Scroller"/> instance.
        /// </summary>
        /// <remarks>
        /// Always use <see cref="ToScrollerPosition(float)"/> when changing the Scroller's position.
        /// </remarks>
        protected Scroller Scroller => cachedScroller ??= GetComponent<Scroller>();

        private float ScrollLength => 1f / Mathf.Max(cellInterval, 1e-2f) - 1f;

        private float ViewportLength => ScrollLength - reuseCellMarginCount * 2f;

        private float PaddingHeadLength => (paddingHead - spacing * 0.5f) / (CellSize + spacing);

        private float MaxScrollPosition => ItemsSource.Count
            - ScrollLength
            + reuseCellMarginCount * 2f
            + (paddingHead + paddingTail - spacing) / (CellSize + spacing);

        /// <inheritdoc/>
        protected override void Initialize()
        {
            base.Initialize();

            Context.ScrollDirection = Scroller.ScrollDirection;
            Context.CalculateScrollSize = CalculateScrollSize;

            AdjustCellIntervalAndScrollOffset();
            Scroller.OnValueChanged(OnScrollerValueChanged);
        }

        private (float ScrollSize, float ReuseMargin) CalculateScrollSize()
        {
            var interval = CellSize + spacing;
            var reuseMargin = interval * reuseCellMarginCount;
            var scrollSize = Scroller.ViewportSize + interval + reuseMargin * 2f;
            return (scrollSize, reuseMargin);
        }

        /// <summary>
        /// Called when the Scroller's position changes.
        /// </summary>
        /// <param name="p">The Scroller's position.</param>
        private void OnScrollerValueChanged(float p)
        {
            base.UpdatePosition(ToFancyScrollViewPosition(Scrollable ? p : 0f));

            if (Scroller.Scrollbar)
            {
                if (p > ItemsSource.Count - 1)
                {
                    ShrinkScrollbar(p - (ItemsSource.Count - 1));
                }
                else if (p < 0f)
                {
                    ShrinkScrollbar(-p);
                }
            }
        }

        /// <summary>
        /// Shrinks the scrollbar based on the over-scroll amount.
        /// </summary>
        /// <param name="offset">The over-scroll amount.</param>
        private void ShrinkScrollbar(float offset)
        {
            var scale = 1f - ToFancyScrollViewPosition(offset) / (ViewportLength - PaddingHeadLength);
            UpdateScrollbarSize((ViewportLength - PaddingHeadLength) * scale);
        }

        /// <inheritdoc/>
        protected override void Refresh()
        {
            AdjustCellIntervalAndScrollOffset();
            RefreshScroller();
            base.Refresh();
        }

        /// <inheritdoc/>
        protected override void Relayout()
        {
            AdjustCellIntervalAndScrollOffset();
            RefreshScroller();
            base.Relayout();
        }

        /// <summary>
        /// Refreshes the Scroller state.
        /// </summary>
        protected void RefreshScroller()
        {
            Scroller.Draggable = Scrollable;
            Scroller.ScrollSensitivity = ToScrollerPosition(ViewportLength - PaddingHeadLength);
            Scroller.Position = ToScrollerPosition(currentPosition);

            if (Scroller.Scrollbar)
            {
                Scroller.Scrollbar.gameObject.SetActive(Scrollable);
                UpdateScrollbarSize(ViewportLength);
            }
        }

        /// <inheritdoc/>
        protected override void UpdateContents(IList<TItemData> items)
        {
            AdjustCellIntervalAndScrollOffset();
            base.UpdateContents(items);

            Scroller.SetTotalCount(items.Count);
            RefreshScroller();
        }

        /// <summary>
        /// Updates the scroll position.
        /// </summary>
        /// <param name="position">The scroll position.</param>
        protected new void UpdatePosition(float position)
        {
            Scroller.Position = ToScrollerPosition(position, 0.5f);
        }

        /// <summary>
        /// Jumps to the specified item index.
        /// </summary>
        /// <param name="itemIndex">The item index.</param>
        /// <param name="alignment">Alignment within the viewport. 0f (head) to 1f (tail).</param>
        protected virtual void JumpTo(int itemIndex, float alignment = 0.5f)
        {
            Scroller.Position = ToScrollerPosition(itemIndex, alignment);
        }

        /// <summary>
        /// Scrolls to the specified item index.
        /// </summary>
        /// <param name="index">The item index.</param>
        /// <param name="duration">Duration of the scroll.</param>
        /// <param name="alignment">Alignment within the viewport. 0f (head) to 1f (tail).</param>
        /// <param name="onComplete">Callback when scroll completes.</param>
        protected virtual void ScrollTo(int index, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            Scroller.ScrollTo(ToScrollerPosition(index, alignment), duration, onComplete);
        }

        /// <summary>
        /// Scrolls to the specified item index with easing.
        /// </summary>
        /// <param name="index">The item index.</param>
        /// <param name="duration">Duration of the scroll.</param>
        /// <param name="easing">Easing function.</param>
        /// <param name="alignment">Alignment within the viewport. 0f (head) to 1f (tail).</param>
        /// <param name="onComplete">Callback when scroll completes.</param>
        protected virtual void ScrollTo(int index, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            Scroller.ScrollTo(ToScrollerPosition(index, alignment), duration, easing, onComplete);
        }

        /// <summary>
        /// Updates the scrollbar size.
        /// </summary>
        protected void UpdateScrollbarSize(float viewportLength)
        {
            var contentLength = Mathf.Max(ItemsSource.Count + (paddingHead + paddingTail - spacing) / (CellSize + spacing), 1);
            Scroller.Scrollbar.size = Scrollable ? Mathf.Clamp01(viewportLength / contentLength) : 1f;
        }

        /// <summary>
        /// Converts FancyScrollRect position to Scroller position.
        /// </summary>
        protected float ToFancyScrollViewPosition(float position)
        {
            return position / Mathf.Max(ItemsSource.Count - 1, 1) * MaxScrollPosition - PaddingHeadLength;
        }

        /// <summary>
        /// Converts Scroller position to FancyScrollRect position.
        /// </summary>
        protected float ToScrollerPosition(float position)
        {
            return (position + PaddingHeadLength) / MaxScrollPosition * Mathf.Max(ItemsSource.Count - 1, 1);
        }

        /// <summary>
        /// Converts Scroller position to FancyScrollRect position with alignment.
        /// </summary>
        protected float ToScrollerPosition(float position, float alignment = 0.5f)
        {
            var offset = alignment * (ScrollLength - (1f + reuseCellMarginCount * 2f))
                + (1f - alignment - 0.5f) * spacing / (CellSize + spacing);
            return ToScrollerPosition(Mathf.Clamp(position - offset, 0f, MaxScrollPosition));
        }

        /// <summary>
        /// Adjusts cell interval and scroll offset.
        /// </summary>
        protected void AdjustCellIntervalAndScrollOffset()
        {
            var totalSize = Scroller.ViewportSize + (CellSize + spacing) * (1f + reuseCellMarginCount * 2f);
            cellInterval = (CellSize + spacing) / totalSize;
            scrollOffset = cellInterval * (1f + reuseCellMarginCount);
        }

        protected virtual void OnValidate()
        {
            AdjustCellIntervalAndScrollOffset();

            if (loop)
            {
                loop = false;
                Debug.LogError("Loop is currently not supported in FancyScrollRect.");
            }

            if (Scroller.SnapEnabled)
            {
                Scroller.SnapEnabled = false;
                Debug.LogError("Snap is currently not supported in FancyScrollRect.");
            }

            if (Scroller.MovementType == MovementType.Unrestricted)
            {
                Scroller.MovementType = MovementType.Elastic;
                Debug.LogError("MovementType.Unrestricted is currently not supported in FancyScrollRect.");
            }
        }
    }

    /// <summary>
    /// Abstract base class for implementing a ScrollRect-style scroll view.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyScrollRect{TItemData, TContext}"/>
    public abstract class FancyScrollRect<TItemData> : FancyScrollRect<TItemData, FancyScrollRectContext> { }
}
