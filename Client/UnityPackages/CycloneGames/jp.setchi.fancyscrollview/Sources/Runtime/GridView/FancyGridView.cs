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
    /// Abstract base class for implementing a grid-layout scroll view.
    /// Does not support infinite scrolling or snapping.
    /// Use <see cref="FancyGridView{TItemData}"/> if <see cref="FancyScrollView{TItemData, TContext}.Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of <see cref="FancyScrollView{TItemData, TContext}.Context"/>.</typeparam>
    public abstract class FancyGridView<TItemData, TContext> : FancyScrollRect<TItemData[], TContext>
        where TContext : class, IFancyGridViewContext, new()
    {
        /// <summary>
        /// Default cell group class.
        /// </summary>
        protected abstract class DefaultCellGroup : FancyCellGroup<TItemData, TContext> { }

        /// <summary>
        /// Spacing in the starting axis.
        /// </summary>
        [SerializeField] protected float startAxisSpacing = 0f;

        /// <summary>
        /// Cell count in the starting axis.
        /// </summary>
        [SerializeField] protected int startAxisCellCount = 4;

        /// <summary>
        /// The size of the cell.
        /// </summary>
        [SerializeField] protected Vector2 cellSize = new Vector2(100f, 100f);

        /// <summary>
        /// The prefab of the cell group.
        /// </summary>
        protected sealed override GameObject CellPrefab => cellGroupTemplate;

        /// <inheritdoc/>
        protected override float CellSize => Scroller.ScrollDirection == ScrollDirection.Horizontal
            ? cellSize.x
            : cellSize.y;

        /// <summary>
        /// The total count of items.
        /// </summary>
        public int DataCount { get; private set; }

        private GameObject cellGroupTemplate;

        // Cached list to avoid reallocation
        private readonly List<TItemData[]> groupedItems = new List<TItemData[]>();

        /// <inheritdoc/>
        protected override void Initialize()
        {
            base.Initialize();

            Debug.Assert(startAxisCellCount > 0);

            Context.ScrollDirection = Scroller.ScrollDirection;
            Context.GetGroupCount = GetGroupCount;
            Context.GetStartAxisSpacing = GetStartAxisSpacing;
            Context.GetCellSize = GetCellSize;

            SetupCellTemplate();
        }

        // Context delegates
        private int GetGroupCount() => startAxisCellCount;
        private float GetStartAxisSpacing() => startAxisSpacing;
        private float GetCellSize() => Scroller.ScrollDirection == ScrollDirection.Horizontal ? cellSize.y : cellSize.x;

        /// <summary>
        /// Called just before the first cell is generated.
        /// Use <see cref="Setup{TGroup}(FancyCell{TItemData, TContext})"/> to setup the cell template.
        /// </summary>
        protected abstract void SetupCellTemplate();

        /// <summary>
        /// Sets up the cell template.
        /// </summary>
        /// <param name="cellTemplate">The cell template.</param>
        /// <typeparam name="TGroup">The type of cell group.</typeparam>
        protected virtual void Setup<TGroup>(FancyCell<TItemData, TContext> cellTemplate)
            where TGroup : FancyCell<TItemData[], TContext>
        {
            Context.CellTemplate = cellTemplate.gameObject;

            cellGroupTemplate = new GameObject("Group").AddComponent<TGroup>().gameObject;
            cellGroupTemplate.transform.SetParent(cellContainer, false);
            cellGroupTemplate.SetActive(false);
        }

        /// <summary>
        /// Updates the content based on the item list.
        /// </summary>
        /// <param name="items">The list of items.</param>
        public virtual void UpdateContents(IList<TItemData> items)
        {
            DataCount = items.Count;

            groupedItems.Clear();

            // Zero-GC grouping implementation replacing LINQ
            if (DataCount > 0)
            {
                int groupCount = (DataCount + startAxisCellCount - 1) / startAxisCellCount;
                if (groupedItems.Capacity < groupCount)
                {
                    groupedItems.Capacity = groupCount;
                }

                for (int i = 0; i < groupCount; i++)
                {
                    int startIndex = i * startAxisCellCount;
                    int count = Math.Min(startAxisCellCount, DataCount - startIndex);
                    
                    var group = new TItemData[count];
                    for (int j = 0; j < count; j++)
                    {
                        group[j] = items[startIndex + j];
                    }
                    groupedItems.Add(group);
                }
            }

            UpdateContents(groupedItems);
        }

        /// <summary>
        /// Jumps to the specified item index.
        /// </summary>
        protected override void JumpTo(int itemIndex, float alignment = 0.5f)
        {
            var groupIndex = itemIndex / startAxisCellCount;
            base.JumpTo(groupIndex, alignment);
        }

        /// <summary>
        /// Scrolls to the specified item index.
        /// </summary>
        protected override void ScrollTo(int itemIndex, float duration, float alignment = 0.5f, Action onComplete = null)
        {
            var groupIndex = itemIndex / startAxisCellCount;
            base.ScrollTo(groupIndex, duration, alignment, onComplete);
        }

        /// <summary>
        /// Scrolls to the specified item index with easing.
        /// </summary>
        protected override void ScrollTo(int itemIndex, float duration, Ease easing, float alignment = 0.5f, Action onComplete = null)
        {
            var groupIndex = itemIndex / startAxisCellCount;
            base.ScrollTo(groupIndex, duration, easing, alignment, onComplete);
        }
    }

    /// <summary>
    /// Abstract base class for implementing a grid-layout scroll view.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyGridView{TItemData, TContext}"/>
    public abstract class FancyGridView<TItemData> : FancyGridView<TItemData, FancyGridViewContext> { }
}
