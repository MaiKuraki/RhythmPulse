using CycloneGames.Service;

namespace RhythmPulse.UI
{
    public class UIAssetPathBuilder : IAssetPathBuilder
    {
        public string GetAssetPath(string UIWindowName)
            => $"Assets/RhythmPulse/LiveContent/ScriptableObjects/UI/Window/{UIWindowName}.asset";
    }
}