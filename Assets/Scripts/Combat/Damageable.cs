using UnityEngine;

namespace Vanguard.Combat
{
    public enum DamageLocation
    {
        Head,
        Torso,
        LeftArm,
        RightArm,
        LeftLeg,
        RightLeg,
        Other
    }

    public readonly struct DamageEvent
    {
        public readonly float Amount;
        public readonly DamageLocation Location;
        public readonly GameObject Source;
        public readonly GameObject DirectSource;
        public readonly Vector3 Position;
        public readonly Vector3 Direction;
        public readonly bool IsFatal;

        public DamageEvent(float amount, DamageLocation location, GameObject source, GameObject directSource,
            Vector3 position, Vector3 direction, bool isFatal)
        {
            Amount = amount;
            Location = location;
            Source = source;
            DirectSource = directSource;
            Position = position;
            Direction = direction;
            IsFatal = isFatal;
        }
    }

    public sealed class Damageable : MonoBehaviour
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private GameObject localPlayerCamera;
        [SerializeField] private AudioClip[] hitSounds;
        [SerializeField] private AudioClip deathSound;

        private float _health;
        private bool _alive = true;

        public event System.Action<DamageEvent> Damaged;
        public event System.Action<GameObject> Killed;

        public float Health => _health;
        public float MaxHealth => maxHealth;
        public bool IsAlive => _alive;
        public bool IsLocalPlayer { get; private set; }

        private void Awake()
        {
            _health = maxHealth;
            Camera camera = Camera.main;
            IsLocalPlayer = camera != null && localPlayerCamera != null
                ? localPlayerCamera == camera.gameObject || localPlayerCamera.transform.IsChildOf(camera.transform)
                : false;
        }

        public void ApplyDamage(DamageEvent damage)
        {
            if (!_alive || damage.Amount <= 0f)
                return;

            _health = Mathf.Max(0f, _health - damage.Amount);
            bool fatal = _health <= 0f;
            DamageEvent finalized = new DamageEvent(
                damage.Amount,
                damage.Location,
                damage.Source,
                damage.DirectSource,
                damage.Position,
                damage.Direction,
                fatal);

            Damaged?.Invoke(finalized);
            if (fatal)
            {
                _alive = false;
                Killed?.Invoke(damage.Source);
            }
        }

        public void Heal(float amount)
        {
            if (!_alive)
                return;
            _health = Mathf.Min(maxHealth, _health + amount);
        }

        public static DamageLocation GetLocationFromCollider(Collider collider)
        {
            if (collider == null)
                return DamageLocation.Other;

            string name = collider.name.ToLowerInvariant();
            if (name.Contains("head") || name.Contains("helmet")) return DamageLocation.Head;
            if (name.Contains("torso") || name.Contains("chest") || name.Contains("spine") || name.Contains("pelvis"))
                return DamageLocation.Torso;
            if (name.Contains("arm") || name.Contains("hand") || name.Contains("shoulder"))
                return name.Contains("left") ? DamageLocation.LeftArm : DamageLocation.RightArm;
            if (name.Contains("leg") || name.Contains("foot") || name.Contains("knee"))
                return name.Contains("left") ? DamageLocation.LeftLeg : DamageLocation.RightLeg;
            return DamageLocation.Other;
        }
    }
}
