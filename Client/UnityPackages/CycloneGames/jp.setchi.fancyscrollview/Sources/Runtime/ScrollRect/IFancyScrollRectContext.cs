/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using System;

namespace FancyScrollView
{
    /// <summary>
    /// Context interface for <see cref="FancyScrollRect{TItemData, TContext}"/>.
    /// </summary>
    public interface IFancyScrollRectContext
    {
        ScrollDirection ScrollDirection { get; set; }
        Func<(float ScrollSize, float ReuseMargin)> CalculateScrollSize { get; set; }
    }
}
