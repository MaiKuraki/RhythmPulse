using CycloneGames.Factory.Runtime;

namespace RhythmPulse.Audio
{
    public sealed class AudioSourceFactory : MonoPrefabFactory<GameAudioSource>
    {
        public AudioSourceFactory(IUnityObjectSpawner spawner, IAudioLoadService audioLoadService)
            : base(spawner, audioLoadService.AudioSourcePrefab)
        {
        }
    }
}