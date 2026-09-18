using System.Collections.Generic;
using UnityEngine;

namespace Vanguard.Utilities
{
    public sealed class PooledLifetime : MonoBehaviour
    {
        [SerializeField] private float lifetime = 1f;
        private float _remaining;

        private void OnEnable()
        {
            _remaining = lifetime;
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_remaining > 0f)
                return;

            gameObject.SetActive(false);
            ObjectPool.Return(gameObject);
        }
    }
}
