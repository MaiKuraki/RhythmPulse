using CycloneGames.Service;
using RhythmPulse.UI;

namespace RhythmPulse
{
    public class AssetPathBuilderFactory : IAssetPathBuilderFactory
    {
        public IAssetPathBuilder Create(string type)
        {
            return type switch
            {
                "UI" => new UIAssetPathBuilder(),
                _ => null
            };
        }
    }
}