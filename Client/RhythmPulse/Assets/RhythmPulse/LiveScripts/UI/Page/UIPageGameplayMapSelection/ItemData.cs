/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using System;
using RhythmPulse.GameplayData.Runtime;

namespace RhythmPulse.UI
{
    //  CAUTION: should not be struct
    class ItemData
    {
        public MapInfo MapInfo { get; }
        public Action OnSelectedEvent { get; private set; }

        public ItemData(MapInfo mapInfo, in Action OnSelectedEvent = null)
        {
            MapInfo = mapInfo;
            this.OnSelectedEvent = OnSelectedEvent;
        }
    }
}
