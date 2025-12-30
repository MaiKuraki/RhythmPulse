/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using System.Collections.Generic;
using UnityEngine;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing the scroll view.
    /// Supports infinite scrolling and snapping.
    /// Use <see cref="FancyScrollView{TItemData}"/> if <see cref="Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of the <see cref="Context"/>.</typeparam>
    public abstract class FancyScrollView<TItemData, TContext> : MonoBehaviour where TContext : class, new()
    {
        /// <summary>
        /// The interval between cells.
        /// </summary>
        [SerializeField, Range(1e-2f, 1f)] protected float cellInterval = 0.2f;

        /// <summary>
        /// The reference position for scrolling.
        /// e.g. If 0.5 is specified, the first cell is placed at the center when the scroll position is 0.
        /// </summary>
        [SerializeField, Range(0f, 1f)] protected float scrollOffset = 0.5f;

        /// <summary>
        /// Whether to loop the cells.
        /// Set to true for infinite scrolling.
        /// </summary>
        [SerializeField] protected bool loop = false;

        /// <summary>
        /// The parent Transform for the cells.
        /// </summary>
        [SerializeField] protected Transform cellContainer = default;

        // Use List<T> directly to avoid interface dispatch overhead
        private readonly List<FancyCell<TItemData, TContext>> _pool = new List<FancyCell<TItemData, TContext>>();

        protected bool initialized;
        protected float currentPosition;

        /// <summary>
        /// The prefab of the cell.
        /// </summary>
        protected abstract GameObject CellPrefab { get; }

        /// <summary>
        /// The list of item data.
        /// </summary>
        protected IList<TItemData> ItemsSource { get; set; } = new List<TItemData>();

        /// <summary>
        /// The instance of <typeparamref name="TContext"/>.
        /// Shared between cells and the scroll view.
        /// </summary>
        protected TContext Context { get; } = new TContext();

        /// <summary>
        /// Initializes the scroll view.
        /// Called just before the first cell is generated.
        /// </summary>
        protected virtual void Initialize() { }

        /// <summary>
        /// Updates the content based on the item list.
        /// </summary>
        /// <param name="itemsSource">The list of items.</param>
        protected virtual void UpdateContents(IList<TItemData> itemsSource)
        {
            ItemsSource = itemsSource;
            Refresh();
        }

        /// <summary>
        /// Forces a layout update of the cells.
        /// </summary>
        protected virtual void Relayout() => UpdatePosition(currentPosition, false);

        /// <summary>
        /// Forces a refresh of the cells' layout and content.
        /// </summary>
        protected virtual void Refresh() => UpdatePosition(currentPosition, true);

        /// <summary>
        /// Updates the scroll position.
        /// </summary>
        /// <param name="position">The scroll position.</param>
        protected virtual void UpdatePosition(float position) => UpdatePosition(position, false);

        private void UpdatePosition(float position, bool forceRefresh)
        {
            if (!initialized)
            {
                Initialize();
                initialized = true;
            }

            currentPosition = position;

            var p = position - scrollOffset / cellInterval;
            var firstIndex = Mathf.CeilToInt(p);
            var firstPosition = (Mathf.Ceil(p) - p) * cellInterval;

            if (firstPosition + _pool.Count * cellInterval < 1f)
            {
                ResizePool(firstPosition);
            }

            UpdateCells(firstPosition, firstIndex, forceRefresh);
        }

        private void ResizePool(float firstPosition)
        {
            Debug.Assert(CellPrefab != null);
            Debug.Assert(cellContainer != null);

            var addCount = Mathf.CeilToInt((1f - firstPosition) / cellInterval) - _pool.Count;
            for (var i = 0; i < addCount; i++)
            {
                var cellObj = Instantiate(CellPrefab, cellContainer);
                var cell = cellObj.GetComponent<FancyCell<TItemData, TContext>>();
                
                if (cell == null)
                {
                    // Cache the exception message to avoid allocation on throw (though throw itself allocates)
                    // In a perfect world we might want to log error instead of throw to avoid crash in release if possible, 
                    // but keeping logic consistent with original for safety.
                    throw new MissingComponentException(
                        $"FancyCell<{typeof(TItemData).Name}, {typeof(TContext).Name}> component not found in {CellPrefab.name}.");
                }

                cell.SetContext(Context);
                cell.Initialize();
                cell.SetVisible(false);
                _pool.Add(cell);
            }
        }

        private void UpdateCells(float firstPosition, int firstIndex, bool forceRefresh)
        {
            var poolCount = _pool.Count;
            var itemsCount = ItemsSource.Count;

            for (var i = 0; i < poolCount; i++)
            {
                var index = firstIndex + i;
                var position = firstPosition + i * cellInterval;
                var cell = _pool[CircularIndex(index, poolCount)];

                if (loop)
                {
                    index = CircularIndex(index, itemsCount);
                }

                if (index < 0 || index >= itemsCount || position > 1f)
                {
                    cell.SetVisible(false);
                    continue;
                }

                if (forceRefresh || cell.Index != index || !cell.IsVisible)
                {
                    cell.Index = index;
                    cell.SetVisible(true);
                    cell.UpdateContent(ItemsSource[index]);
                }

                cell.UpdatePosition(position);
            }
        }

        private int CircularIndex(int i, int size) => size < 1 ? 0 : i < 0 ? size - 1 + (i + 1) % size : i % size;

#if UNITY_EDITOR
        private bool _cachedLoop;
        private float _cachedCellInterval, _cachedScrollOffset;

        private void LateUpdate()
        {
            if (_cachedLoop != loop ||
                !Mathf.Approximately(_cachedCellInterval, cellInterval) ||
                !Mathf.Approximately(_cachedScrollOffset, scrollOffset))
            {
                _cachedLoop = loop;
                _cachedCellInterval = cellInterval;
                _cachedScrollOffset = scrollOffset;

                UpdatePosition(currentPosition);
            }
        }
#endif
    }

    /// <summary>
    /// Context class for <see cref="FancyScrollView{TItemData}"/>.
    /// </summary>
    public sealed class NullContext { }

    /// <summary>
    /// Abstract base class for implementing the scroll view.
    /// Supports infinite scrolling and snapping.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyScrollView{TItemData, TContext}"/>
    public abstract class FancyScrollView<TItemData> : FancyScrollView<TItemData, NullContext> { }
}
