/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using UnityEngine;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing a cell group containing multiple <see cref="FancyCell{TItemData, TContext}"/>.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class FancyCellGroup<TItemData, TContext> : FancyCell<TItemData[], TContext>
        where TContext : class, IFancyCellGroupContext, new()
    {
        /// <summary>
        /// Array of cells displayed in this group.
        /// </summary>
        protected virtual FancyCell<TItemData, TContext>[] Cells { get; private set; }

        /// <summary>
        /// Instantiates the array of cells.
        /// </summary>
        protected virtual FancyCell<TItemData, TContext>[] InstantiateCells()
        {
            var count = Context.GetGroupCount();
            var cells = new FancyCell<TItemData, TContext>[count];

            for (var i = 0; i < count; i++)
            {
                var cellObj = Instantiate(Context.CellTemplate, transform);
                cells[i] = cellObj.GetComponent<FancyCell<TItemData, TContext>>();
            }

            return cells;
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Cells = InstantiateCells();
            Debug.Assert(Cells.Length == Context.GetGroupCount());

            for (var i = 0; i < Cells.Length; i++)
            {
                Cells[i].SetContext(Context);
                Cells[i].Initialize();
            }
        }

        /// <inheritdoc/>
        public override void UpdateContent(TItemData[] contents)
        {
            var firstCellIndex = Index * Context.GetGroupCount();

            for (var i = 0; i < Cells.Length; i++)
            {
                Cells[i].Index = i + firstCellIndex;
                Cells[i].SetVisible(i < contents.Length);

                if (Cells[i].IsVisible)
                {
                    Cells[i].UpdateContent(contents[i]);
                }
            }
        }

        /// <inheritdoc/>
        public override void UpdatePosition(float position)
        {
            for (var i = 0; i < Cells.Length; i++)
            {
                Cells[i].UpdatePosition(position);
            }
        }
    }
}
