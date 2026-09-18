using UnityEngine;

namespace Vanguard.Utilities
{
    public sealed class ObjectPool : MonoBehaviour
    {
        [System.Serializable]
        private sealed class PoolEntry
        {
            public GameObject Prefab;
            public int Capacity = 16;
        }

        [SerializeField] private PoolEntry[] pools;
        private static ObjectPool _instance;
        private readonly System.Collections.Generic.Dictionary<GameObject, Queue<GameObject>> _available =
            new System.Collections.Generic.Dictionary<GameObject, Queue<GameObject>>();
        private readonly System.Collections.Generic.Dictionary<GameObject, GameObject> _instanceToPrefab =
            new System.Collections.Generic.Dictionary<GameObject, GameObject>();

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            foreach (PoolEntry entry in pools)
            {
                if (entry.Prefab == null || entry.Capacity <= 0)
                    continue;
                Queue<GameObject> queue = new Queue<GameObject>(entry.Capacity);
                for (int i = 0; i < entry.Capacity; i++)
                {
                    GameObject instance = Instantiate(entry.Prefab, transform);
                    instance.SetActive(false);
                    queue.Enqueue(instance);
                }
                _available[entry.Prefab] = queue;
                foreach (GameObject instance in queue)
                    _instanceToPrefab[instance] = entry.Prefab;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        public static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
                return null;
            if (_instance == null)
                return UnityEngine.Object.Instantiate(prefab, position, rotation);

            if (!_instance._available.TryGetValue(prefab, out Queue<GameObject> queue) || queue.Count == 0)
                return UnityEngine.Object.Instantiate(prefab, position, rotation);

            GameObject instance = queue.Dequeue();
            _instanceToPrefab.Remove(instance);
            instance.transform.SetParent(null, false);
            instance.transform.position = position;
            instance.transform.rotation = rotation;
            instance.SetActive(true);
            return instance;
        }

        public static void Return(GameObject instance)
        {
            if (instance == null || _instance == null)
            {
                if (instance != null)
                    UnityEngine.Object.Destroy(instance);
                return;
            }

            instance.SetActive(false);
            instance.transform.SetParent(_instance.transform, false);
            if (_instance._instanceToPrefab.TryGetValue(instance, out GameObject prefab) &&
                _instance._available.TryGetValue(prefab, out Queue<GameObject> queue))
            {
                queue.Enqueue(instance);
                _instance._instanceToPrefab[instance] = prefab;
            }
            else
                UnityEngine.Object.Destroy(instance);
        }

        private void OnDisable()
        {
            if (_instance == this)
                _instance = null;
        }
    }
}
