using UnityEngine;

namespace RhythmPulse.Misc
{
    public class DontDestroyOnLoad : MonoBehaviour
    {
        void Awake()
        {
            DontDestroyOnLoad(transform);
        }
    }
}