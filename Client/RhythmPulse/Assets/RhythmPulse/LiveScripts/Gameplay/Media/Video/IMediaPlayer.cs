namespace RhythmPulse.Media
{
    public interface IMediaPlayer
    {
        void Play();
        void Stop();
        void Pause();
        void Resume();
        long GetPlaybackTimeMSec();
        long GetMediaDurationMSec();
        void SeekTime(long milliSeconds);
    }
}