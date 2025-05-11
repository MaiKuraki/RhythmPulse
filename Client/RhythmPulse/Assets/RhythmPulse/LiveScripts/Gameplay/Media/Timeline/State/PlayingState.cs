using CycloneGames.Logger;

namespace RhythmPulse.Gameplay.Media
{
    public class PlayingState : TimelineState
    {
        private const int AVSyncErrorTime = 50;
        private const int AVSyncFrequency = 2; // 2 seconds
        private float currentFrequency = 0;

        public PlayingState(Timeline timeline) : base(timeline)
        {

        }

        public override void OnEnter()
        {
            CLogger.LogInfo("[Timeline] Enter Playing State");

            if (_timeline.PlaybackTimeMSec < 2) // TODO: Maybe 2ms is not accurate enough for PlayFromStart
            {
                _timeline.OnStartedPlayAction?.Invoke();
            }
            else
            {
                _timeline.OnResumedPlayAction?.Invoke();
            }
        }

        public override void OnExit()
        {
            CLogger.LogInfo("[Timeline] Exit Playing State");
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            long audioPlaybackTimeMsec = 0;

            _timeline.SetPlaybackTimeMSec(audioPlaybackTimeMsec);

            // Check AVOffset every 2 seconds
            if (currentFrequency >= AVSyncFrequency)
            {
                AVSync(audioPlaybackTimeMsec);
                currentFrequency = 0;
            }
            currentFrequency += UnityEngine.Time.deltaTime;
        }

        private void AVSync(long audioPlaybackTimeMSec)
        {
            if (ShouldAVSync())
            {
                long videoPlaybackTimeMSec = 0; // get video playback time from video player
                if (UnityEngine.Mathf.Abs(audioPlaybackTimeMSec - videoPlaybackTimeMSec) > AVSyncErrorTime /* && !_timeline.VideoPlayer.IsSeeking */)
                {
                    //  TODO: 
                    // _timeline.VideoPlayer.Seek(audioPlaybackTimeMSec);
                    CLogger.LogWarning($"[Timeline] AVSync, audioTime:{audioPlaybackTimeMSec} videoTime: {videoPlaybackTimeMSec}");
                }
            }
        }

        private bool ShouldAVSync()
        {
            return true;
        }
    }
}