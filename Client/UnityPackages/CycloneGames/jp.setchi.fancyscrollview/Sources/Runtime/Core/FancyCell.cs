/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using UnityEngine;

namespace FancyScrollView
{
    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyScrollView{TItemData, TContext}"/>.
    /// Use <see cref="FancyCell{TItemData}"/> if <see cref="Context"/> is not needed.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <typeparam name="TContext">The type of the context.</typeparam>
    public abstract class FancyCell<TItemData, TContext> : MonoBehaviour where TContext : class, new()
    {
        private GameObject _cachedGameObject;
        
        /// <summary>
        /// Cached reference to the GameObject to avoid repeated native calls.
        /// </summary>
        protected GameObject CachedGameObject => _cachedGameObject ??= gameObject;

        /// <summary>
        /// The index of the data displayed in this cell.
        /// </summary>
        public int Index { get; set; } = -1;

        /// <summary>
        /// The visibility state of this cell.
        /// </summary>
        public virtual bool IsVisible => CachedGameObject.activeSelf;

        /// <summary>
        /// Reference to the <see cref="Context"/>.
        /// Shared instance between cells and the scroll view.
        /// </summary>
        protected TContext Context { get; private set; }

        /// <summary>
        /// Sets the context.
        /// </summary>
        /// <param name="context">The context.</param>
        public virtual void SetContext(TContext context) => Context = context;

        /// <summary>
        /// Initializes the cell.
        /// </summary>
        public virtual void Initialize() { }

        /// <summary>
        /// Sets the visibility of this cell.
        /// </summary>
        /// <param name="visible">True if visible, otherwise false.</param>
        public virtual void SetVisible(bool visible) => CachedGameObject.SetActive(visible);

        /// <summary>
        /// Updates the content of this cell based on the item data.
        /// </summary>
        /// <param name="itemData">The item data.</param>
        public abstract void UpdateContent(TItemData itemData);

        /// <summary>
        /// Updates the scroll position of this cell based on the value between 0.0f and 1.0f.
        /// </summary>
        /// <param name="position">Normalized scroll position in the viewport.</param>
        public abstract void UpdatePosition(float position);
    }

    /// <summary>
    /// Abstract base class for implementing a cell in <see cref="FancyScrollView{TItemData}"/>.
    /// </summary>
    /// <typeparam name="TItemData">The type of item data.</typeparam>
    /// <seealso cref="FancyCell{TItemData, TContext}"/>
    public abstract class FancyCell<TItemData> : FancyCell<TItemData, NullContext>
    {
        /// <inheritdoc/>
        public sealed override void SetContext(NullContext context) => base.SetContext(context);
    }
}
