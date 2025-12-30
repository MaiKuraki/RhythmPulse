/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using UnityEngine;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyGridView{TItemData, TContext}"/>.
    /// Use <see cref="FancyGridViewCell{TItemData}"/> if <see cref="Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class FancyGridViewCell<TItemData, TContext> : FancyScrollRectCell<TItemData, TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        /// <inheritdoc/>
        protected override void UpdatePosition(float normalizedPosition, float localPosition)
        {
            var cellSize = Context.GetCellSize();
            var spacing = Context.GetStartAxisSpacing();
            var groupCount = Context.GetGroupCount();

            var indexInGroup = Index % groupCount;
            var positionInGroup = (cellSize + spacing) * (indexInGroup - (groupCount - 1) * 0.5f);

            if (Context.ScrollDirection == ScrollDirection.Horizontal)
            {
                transform.localPosition = new Vector2(-localPosition, -positionInGroup);
            }
            else
            {
                transform.localPosition = new Vector2(positionInGroup, localPosition);
            }
        }
    }

    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyGridView{TItemData}"/>.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyGridViewCell{TItemData, TContext}"/>
    public abstract class FancyGridViewCell<TItemData> : FancyGridViewCell<TItemData, FancyGridViewContext>
    {
        /// <inheritdoc/>
        public sealed override void SetContext(FancyGridViewContext context) => base.SetContext(context);
    }
}
