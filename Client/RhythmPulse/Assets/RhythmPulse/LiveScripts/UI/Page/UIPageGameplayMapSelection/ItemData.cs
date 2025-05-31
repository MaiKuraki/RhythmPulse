/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using System;

namespace RhythmPulse.UI
{
    //  CAUTION: should not be struct
    class ItemData
    {
        public string Message { get; }
        public Action OnSelectedEvent { get; private set; }

        public ItemData(string message, in Action OnSelectedEvent = null)
        {
            Message = message;
            this.OnSelectedEvent = OnSelectedEvent;
        }
    }
}
