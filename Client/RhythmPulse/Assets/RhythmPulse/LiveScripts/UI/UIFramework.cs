using UnityEngine;

namespace RhythmPulse.UI
{
    public class UIFramework : MonoBehaviour
    {
        public static UIFramework Instance { get; private set; }

        void Awake()
        {
            MakeUnique();
        }

        void MakeUnique()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                return;
            }

            Destroy(gameObject);
        }
    }
}