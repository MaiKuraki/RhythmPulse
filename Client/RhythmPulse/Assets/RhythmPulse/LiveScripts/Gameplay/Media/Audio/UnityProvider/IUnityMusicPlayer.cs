namespace RhythmPulse.Media
{
    public interface IUnityMusicPlayer : IMediaPlayer
    {
        void InitializeMusicPlayer(in string InAudioKey, bool bLoop = false);
        bool IsAnyAudioInitialized { get; }
    }
}