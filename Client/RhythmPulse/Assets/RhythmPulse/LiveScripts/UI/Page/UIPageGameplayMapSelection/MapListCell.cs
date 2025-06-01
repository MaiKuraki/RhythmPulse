/*
 * FancyScrollView (https://github.com/setchi/FancyScrollView)
 * Copyright (c) 2020 setchi
 * Licensed under MIT (https://github.com/setchi/FancyScrollView/blob/master/LICENSE)
 */

using CycloneGames.Logger;
using FancyScrollView;
using R3;
using R3.Triggers;
using UnityEngine;
using UnityEngine.UI;

namespace RhythmPulse.UI
{
    class MapListCell : FancyCell<ItemData, MapListContext>
    {
        private const string DEBUG_FLAG = "[Cell]";
        [SerializeField] Transform focusFlagTF = default;
        [SerializeField] Animator animator = default;
        [SerializeField] Text message = default;
        [SerializeField] Image image = default;
        [SerializeField] Button button = default;

        private ItemData cachedItemData;
        private bool isIndexSelected;
        private bool isFocused;         //  To avoid multiple element in screen, the indexSelected may be not only one, this flag can filter it

        static class AnimatorHash
        {
            public static readonly int Scroll = Animator.StringToHash("scroll");
        }

        void Awake()
        {
            focusFlagTF.OnEnableAsObservable().Subscribe(_ => { OnFocusChanged(true); }).AddTo(this);
            focusFlagTF.OnDisableAsObservable().Subscribe(_ => { OnFocusChanged(false); }).AddTo(this);
        }

        void OnDestroy()
        {
            cachedItemData = null;
        }

        void Start()
        {
            button.onClick.AddListener(OnSelected);
        }

        private void OnFocusChanged(bool isFocused)
        {
            this.isFocused = isFocused;
        }

        public override void UpdateContent(ItemData itemData)
        {
            cachedItemData = itemData;
            message.text = itemData.MapInfo.DisplayName;
            isIndexSelected = Context.SelectedIndex == Index;

            if (isIndexSelected && isFocused)
            {
                OnSelected();
            }
        }

        private void OnSelected()
        {
            Context.OnCellClicked?.Invoke(Index);
            cachedItemData.OnSelectedEvent?.Invoke();
            CLogger.LogInfo($"{DEBUG_FLAG} Selected {cachedItemData.MapInfo}, index: {Index}");
        }

        public override void UpdatePosition(float position)
        {
            currentPosition = position;

            if (animator.isActiveAndEnabled)
            {
                animator.Play(AnimatorHash.Scroll, -1, position);
            }

            animator.speed = 0;
        }

        // GameObject が非アクティブになると Animator がリセットされてしまうため
        // 現在位置を保持しておいて OnEnable のタイミングで現在位置を再設定します
        float currentPosition = 0;

        void OnEnable() => UpdatePosition(currentPosition);
    }
}
