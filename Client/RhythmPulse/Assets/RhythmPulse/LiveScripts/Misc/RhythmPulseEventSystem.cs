using UnityEngine;

namespace RhythmPulse.Misc
{
    public class RhythmPulseEventSystem : MonoBehaviour
    {
        public static RhythmPulseEventSystem Instance { get; private set; }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                return;
            }

            Destroy(gameObject);
        }

        void MakeUnique()
        {

        }
    }
}