using System.Collections.Generic;
using UnityEngine;
using EasingCore;
using FancyScrollView;
using Cysharp.Threading.Tasks;
using System.Threading;
using System;

namespace RhythmPulse.UI
{
    class MapListScrollView : FancyScrollView<ItemData, MapListContext>
    {
        private const string DEBUG_FLAG = "[MapListScrollView]";
        [SerializeField] Scroller scroller = default;
        [SerializeField] GameObject cellPrefab = default;
        public Action<ItemData> OnSelectedEvent;
        protected override GameObject CellPrefab => cellPrefab;
        private CancellationTokenSource cancelOnSelection;

        protected override void Initialize()
        {
            base.Initialize();

            Context.OnCellClicked = SelectCell;

            scroller.OnValueChanged(UpdatePosition);
            scroller.OnSelectionChanged(UpdateSelection);
        }

        public void SetCellInterval(float interval)
        {
            cellInterval = interval;
        }

        void UpdateSelection(int index)
        {
            if (Context.SelectedIndex == index)
            {
                return;
            }

            Context.SelectedIndex = index;
            Refresh();

            if (cancelOnSelection != null)
            {
                cancelOnSelection.Cancel();
                cancelOnSelection.Dispose();
            }
            cancelOnSelection = new CancellationTokenSource();
            OnSelectionChangedAsync(index, cancelOnSelection).Forget();
        }

        public void UpdateData(IList<ItemData> items)
        {
            UpdateContents(items);
            scroller.SetTotalCount(items.Count);
        }

        public void SelectCell(int index)
        {
            if (index < 0 || index >= ItemsSource.Count || index == Context.SelectedIndex)
            {
                return;
            }

            UpdateSelection(index);
            scroller.ScrollTo(index, 0.35f, Ease.OutCubic);
        }

        public async UniTask ForceUpdateSelectionAsync(int index, CancellationTokenSource cancellationTokenSource)
        {
            Refresh();
            await OnSelectionChangedAsync(index, cancellationTokenSource);
            // CLogger.LogInfo($"{DEBUG_FLAG} ForceUpdateSelection {index}, data: {data.Message}");
        }

        private async UniTask OnSelectionChangedAsync(int index, CancellationTokenSource cancellationTokenSource)
        {
            await UniTask.WaitUntil(() => index >= 0 && ItemsSource.Count > index && ItemsSource[index] != null, PlayerLoopTiming.Update, cancellationToken: cancellationTokenSource.Token);
            if (cancellationTokenSource != null && cancellationTokenSource.IsCancellationRequested) return;
            ItemData data = ItemsSource[index];
            // OnUpdateMapDisplayName?.Invoke(data.MapInfo.DisplayName);
            OnSelectedEvent?.Invoke(data);
        }
    }
}
