using CycloneGames.Factory.Runtime;

namespace RhythmPulse.Audio
{
    public class AudioSourceFactory : MonoPrefabFactory<GameAudioSource>
    {
        public AudioSourceFactory(IUnityObjectSpawner spawner, IAudioLoadService audioLoadService)
            : base(spawner, audioLoadService.AudioSourcePrefab) //  Note: the prefab is set in the AudioLoadService
        {

        }
    }
}