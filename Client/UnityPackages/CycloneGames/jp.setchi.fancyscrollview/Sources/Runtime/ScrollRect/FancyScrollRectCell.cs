/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using UnityEngine;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyScrollRect{TItemData, TContext}"/>.
    /// Use <see cref="FancyScrollRectCell{TItemData}"/> if <see cref="Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class FancyScrollRectCell<TItemData, TContext> : FancyCell<TItemData, TContext>
        where TContext : class, IFancyScrollRectContext, new()
    {
        /// <inheritdoc/>
        public override void UpdatePosition(float position)
        {
            var (scrollSize, reuseMargin) = Context.CalculateScrollSize();

            var normalizedPosition = (Mathf.Lerp(0f, scrollSize, position) - reuseMargin) / (scrollSize - reuseMargin * 2f);

            var start = 0.5f * scrollSize;
            var end = -start;

            UpdatePosition(normalizedPosition, Mathf.Lerp(start, end, position));
        }

        /// <summary>
        /// Updates the cell position.
        /// </summary>
        /// <param name="normalizedPosition">Normalized scroll position within the viewport.</param>
        /// <param name="localPosition">Local position.</param>
        protected virtual void UpdatePosition(float normalizedPosition, float localPosition)
        {
            if (Context.ScrollDirection == ScrollDirection.Horizontal)
            {
                transform.localPosition = new Vector2(-localPosition, 0);
            }
            else
            {
                transform.localPosition = new Vector2(0, localPosition);
            }
        }
    }

    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyScrollRect{TItemData}"/>.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyScrollRectCell{TItemData, TContext}"/>
    public abstract class FancyScrollRectCell<TItemData> : FancyScrollRectCell<TItemData, FancyScrollRectContext>
    {
        /// <inheritdoc/>
        public sealed override void SetContext(FancyScrollRectContext context) => base.SetContext(context);
    }
}
