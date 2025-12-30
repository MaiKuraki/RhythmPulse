namespace RhythmPulse.Media
{
    public sealed class PlayingState : TimelineState
    {
        private const int AVSyncThresholdMs = 50;
        private const double AVSyncIntervalSeconds = 2.0;
        
        private double _timeSinceLastSync;

        public PlayingState(Timeline timeline) : base(timeline) { }

        public override void OnEnter()
        {
            _timeSinceLastSync = 0;

            if (Timeline.PlaybackTimeMSec < 2)
                Timeline.RaiseStartedPlay();
            else
                Timeline.RaiseResumedPlay();
        }

        public override void OnExit() { }

        public override void OnUpdate()
        {
            long audioPlaybackTimeMsec = Timeline.UnityMusicPlayer.GetPlaybackTimeMSec();
            Timeline.SetPlaybackTimeMSec(audioPlaybackTimeMsec);

            _timeSinceLastSync += UnityEngine.Time.deltaTime;
            if (_timeSinceLastSync >= AVSyncIntervalSeconds)
            {
                SyncAudioVideo(audioPlaybackTimeMsec);
                _timeSinceLastSync = 0;
            }
        }

        private void SyncAudioVideo(long audioPlaybackTimeMSec)
        {
            if (Timeline.UnityVideoPlayer == null) return;

            long videoPlaybackTimeMSec = Timeline.UnityVideoPlayer.GetPlaybackTimeMSec();
            long diff = audioPlaybackTimeMSec - videoPlaybackTimeMSec;
            
            if (diff > AVSyncThresholdMs || diff < -AVSyncThresholdMs)
            {
                Timeline.UnityVideoPlayer.SeekTime(audioPlaybackTimeMSec);
            }
        }
    }
}