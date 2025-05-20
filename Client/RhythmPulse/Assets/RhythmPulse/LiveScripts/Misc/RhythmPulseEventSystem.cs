using UnityEngine;

namespace RhythmPulse.Misc
{
    public class RhythmPulseEventSystem : MonoBehaviour
    {
        public static RhythmPulseEventSystem Instance { get; private set; }

        [SerializeField] private bool _singleton = true;

        void Awake()
        {
            if (_singleton)
            {
                if (Instance != null && Instance != this)
                {
                    Destroy(gameObject);
                    return;
                }

                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
        }
    }
}