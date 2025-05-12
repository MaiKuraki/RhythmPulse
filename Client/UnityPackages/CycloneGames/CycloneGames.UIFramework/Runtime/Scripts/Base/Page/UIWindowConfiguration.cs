using UnityEngine;

namespace CycloneGames.UIFramework
{
    [CreateAssetMenu(menuName = "CycloneGames/UIFramework/UIWindow")]
    [System.Serializable]
    public class UIWindowConfiguration : ScriptableObject
    {
        //TODO: Maybe there is a better way to implement this, to resolve the dependency of UIPageConfiguration and UIPage
        [SerializeField] private UIWindow windowPrefab;
        [SerializeField] private UILayerConfiguration layer;

        public UIWindow WindowPrefab => windowPrefab;
        public UILayerConfiguration Layer => layer;
    }
}