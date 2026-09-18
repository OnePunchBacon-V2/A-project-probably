using UnityEngine;

namespace Vanguard.Combat
{
    public enum FireMode
    {
        Automatic,
        SemiAutomatic,
        Burst,
        Pump
    }

    public enum AmmoType
    {
        Rifle,
        Pistol,
        Shotgun,
        Sniper,
        Energy
    }

    [System.Serializable]
    public sealed class WeaponDamageRange
    {
        public float Distance = 20f;
        [Range(1f, 200f)] public float Damage = 32f;
    }

    public sealed class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "New Weapon";
        public string codename = "new_weapon";
        public Sprite icon;
        public AmmoType ammoType;
        public FireMode fireMode = FireMode.Automatic;

        [Header("Combat")]
        public float rateOfFire = 600f;
        public WeaponDamageRange[] damageRanges = {
            new WeaponDamageRange { Distance = 18f, Damage = 32f },
            new WeaponDamageRange { Distance = 45f, Damage = 25f },
            new WeaponDamageRange { Distance = 9999f, Damage = 19f }
        };
        [Range(1f, 10f)] public float headMultiplier = 1.5f;
        [Range(0.1f, 1f)] public float limbMultiplier = 0.8f;
        public int magazineSize = 30;
        public int startingReserve = 90;
        public float reloadTime = 1.75f;
        public float equipTime = 0.35f;
        public float inspectTime = 1.5f;

        [Header("Handling")]
        public float adsTime = 0.18f;
        public float hipFireTime = 0.08f;
        public float moveSpeedMultiplier = 1f;
        public float sprintToFireDelay = 0.18f;
        public float hipSpreadDegrees = 2.2f;
        public float adsSpreadDegrees = 0.08f;
        public float spreadWhileMoving = 1.2f;
        public float recoilHorizontal = 0.08f;
        public Vector2 recoilVertical = new Vector2(0.22f, 0.38f);
        public float recoilRecoveryDelay = 0.08f;

        [Header("Feedback")]
        public GameObject muzzleFlashPrefab;
        public GameObject impactPrefab;
        public GameObject shellPrefab;
        public Transform shellEjectionPoint;
        public AudioClip[] fireSounds;
        public AudioClip suppressedFireSound;
        public AudioClip dryFireSound;
        public AudioClip reloadSound;
        public AudioClip equipSound;
        public AudioClip tailSound;

        public float GetDamage(float distance)
        {
            if (damageRanges == null || damageRanges.Length == 0)
                return 0f;

            for (int i = 0; i < damageRanges.Length; i++)
            {
                if (distance <= damageRanges[i].Distance)
                    return damageRanges[i].Damage;
            }

            return damageRanges[^1].Damage;
        }
    }

    public sealed class Weapon : MonoBehaviour
    {
        [Header("Definition")]
        [SerializeField] private WeaponDefinition definition;
        [SerializeField] private Transform muzzle;
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private LayerMask targetMask = ~0;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private float maxRange = 300f;

        [Header("State")]
        [SerializeField] private int magazineAmmo;
        [SerializeField] private int reserveAmmo;
        [SerializeField] private bool isPrimary = true;

        private float _nextFireTime;
        private float _reloadEndTime;
        private float _equipEndTime;
        private float _adsAmount;
        private float _recoilRecoverTime;
        private Vector2 _accumulatedRecoil;
        private int _burstShots;
        private float _burstResetTime;
        private bool _firing;
        private bool _reloading;

        public WeaponDefinition Definition => definition;
        public int MagazineAmmo => magazineAmmo;
        public int ReserveAmmo => reserveAmmo;
        public bool IsPrimary => isPrimary;
        public bool IsReloading => _reloading;
        public bool IsEquipped { get; private set; }
        public event System.Action<Weapon> WeaponStateChanged;
        public event System.Action<Weapon, RaycastHit, DamageLocation> HitRegistered;
        public event System.Action<Weapon> DryFired;
        public event System.Action<Weapon> ReloadStarted;
        public event System.Action<Weapon> ReloadCompleted;

        private void Awake()
        {
            if (definition == null)
            {
                enabled = false;
                return;
            }

            magazineAmmo = definition.magazineSize;
            reserveAmmo = definition.startingReserve;
            _equipEndTime = Time.time + definition.equipTime;
            IsEquipped = true;
        }

        private void Update()
        {
            UpdateAimDownSights();
            UpdateReload();
            UpdateFireInput();
        }

        private void UpdateAimDownSights()
        {
            bool aiming = Input.GetButton("Aim");
            float speed = 1f / (aiming ? definition.adsTime : definition.hipFireTime);
            _adsAmount = Mathf.MoveTowards(_adsAmount, aiming ? 1f : 0f, speed * Time.deltaTime);
        }

        private void UpdateReload()
        {
            if (!_reloading)
                return;

            if (Time.time >= _reloadEndTime)
            {
                int missing = definition.magazineSize - magazineAmmo;
                int transfer = Mathf.Min(missing, reserveAmmo);
                magazineAmmo += transfer;
                reserveAmmo -= transfer;
                _reloading = false;
                ReloadCompleted?.Invoke(this);
                WeaponStateChanged?.Invoke(this);
            }
        }

        private void UpdateFireInput()
        {
            if (_reloading || Time.time < _equipEndTime || Time.time < _recoilRecoverTime)
                return;

            bool fireHeld = Input.GetButton("Fire1");
            bool firePressed = Input.GetButtonDown("Fire1");
            if (!fireHeld && !firePressed)
            {
                _firing = false;
                _burstShots = 0;
                return;
            }

            if (definition.fireMode == FireMode.SemiAutomatic && !firePressed)
                return;

            if (definition.fireMode == FireMode.Burst)
            {
                if (!firePressed && _burstShots == 0)
                    return;
                if (_burstShots > 0 && Time.time >= _burstResetTime)
                    _burstShots = 0;
            }

            if (Time.time < _nextFireTime)
                return;

            TryFire();
        }

        private void TryFire()
        {
            if (magazineAmmo <= 0)
            {
                PlayOneShot(definition.dryFireSound);
                DryFired?.Invoke(this);
                StartReload();
                return;
            }

            magazineAmmo--;
            _nextFireTime = Time.time + 60f / Mathf.Max(1f, definition.rateOfFire);
            FireHitscan();

            if (definition.fireMode == FireMode.Burst)
            {
                _burstShots++;
                _burstResetTime = Time.time + 0.35f;
                if (_burstShots >= 3)
                    _burstShots = 0;
            }

            WeaponStateChanged?.Invoke(this);
        }

        private void FireHitscan()
        {
            if (cameraTransform == null)
                cameraTransform = Camera.main != null ? Camera.main.transform : transform;

            Vector3 origin = cameraTransform.position;
            Vector3 forward = cameraTransform.forward;
            float spreadDegrees = Mathf.Lerp(definition.hipSpreadDegrees, definition.adsSpreadDegrees, _adsAmount);
            spreadDegrees += definition.spreadWhileMoving * Mathf.Clamp01(GetMovementSpeedRatio());
            forward = ApplySpread(forward, spreadDegrees);

            Vector3 end = origin + forward * maxRange;
            if (Physics.Raycast(origin, forward, out RaycastHit hit, maxRange, targetMask, QueryTriggerInteraction.Ignore))
                end = hit.point;

            if (Physics.Linecast(origin, end, out RaycastHit obstacle, obstacleMask) &&
                (!hit.collider || obstacle.distance < hit.distance))
            {
                SpawnImpact(obstacle.point, obstacle.normal);
                return;
            }

            if (hit.collider == null)
            {
                SpawnImpact(end, -forward);
                return;
            }

            Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
            if (damageable != null)
            {
                DamageLocation location = Damageable.GetLocationFromCollider(hit.collider);
                float damage = definition.GetDamage(hit.distance);
                if (location == DamageLocation.Head)
                    damage *= definition.headMultiplier;
                else if (location is DamageLocation.LeftArm or DamageLocation.RightArm or
                    DamageLocation.LeftLeg or DamageLocation.RightLeg)
                    damage *= definition.limbMultiplier;

                Vector3 direction = (hit.point - origin).normalized;
                damageable.ApplyDamage(new DamageEvent(damage, location, gameObject, gameObject,
                    hit.point, direction, false));
                HitRegistered?.Invoke(this, hit, location);
            }

            SpawnImpact(hit.point, hit.normal);
            ApplyRecoil();
            EjectShell();
            SpawnMuzzleFlash();
            PlayOneShot(definition.fireSounds != null && definition.fireSounds.Length > 0
                ? definition.fireSounds[Random.Range(0, definition.fireSounds.Length)]
                : null);
            PlayOneShot(definition.tailSound, 0.25f);
        }

        private Vector3 ApplySpread(Vector3 direction, float degrees)
        {
            if (degrees <= 0f)
                return direction;

            Vector2 circle = Random.insideUnitCircle * degrees * 0.5f;
            Quaternion spreadRotation = Quaternion.Angle(circle.x, cameraTransform.up) *
                                        Quaternion.Angle(circle.y, cameraTransform.right);
            return spreadRotation * direction;
        }

        private float GetMovementSpeedRatio()
        {
            FirstPerson.Player.FirstPersonController controller =
                GetComponentInParent<FirstPerson.Player.FirstPersonController>();
            return controller != null ? Mathf.Clamp01(controller.CurrentHorizontalSpeed() / 7f) : 0f;
        }

        private void ApplyRecoil()
        {
            Vector2 vertical = Random.Range(definition.recoilVertical.x, definition.recoilVertical.y) * Vector2.up;
            Vector2 horizontal = Random.Range(-definition.recoilHorizontal, definition.recoilHorizontal) * Vector2.right;
            Vector2 recoil = vertical + horizontal;
            _accumulatedRecoil += recoil;
            _recoilRecoverTime = Time.time + definition.recoilRecoveryDelay;

            FirstPerson.Player.FirstPersonController controller =
                GetComponentInParent<FirstPerson.Player.FirstPersonController>();
            controller?.AddRecoil(recoil * 0.12f);

            Transform viewmodel = transform;
            viewmodel.localPosition += Vector3.up * recoil.x * 0.025f;
            viewmodel.localRotation *= Quaternion.Euler(-recoil.x * 2f, 0f, 0f);
        }

        private void Update()
        {
        }

        private void OnEnable()
        {
            _equipEndTime = Time.time + definition.equipTime;
            IsEquipped = true;
            WeaponStateChanged?.Invoke(this);
        }

        private void OnDisable()
        {
            IsEquipped = false;
            _firing = false;
        }

        public void StartReload()
        {
            if (_reloading || magazineAmmo == definition.magazineSize || reserveAmmo <= 0)
                return;

            _reloading = true;
            _reloadEndTime = Time.time + definition.reloadTime;
            ReloadStarted?.Invoke(this);
            WeaponStateChanged?.Invoke(this);
            PlayOneShot(definition.reloadSound);
        }

        public void CancelReload()
        {
            if (!_reloading)
                return;
            _reloading = false;
            WeaponStateChanged?.Invoke(this);
        }

        public void AddAmmo(int magazine, int reserve)
        {
            int addedMagazine = Mathf.Min(definition.magazineSize - magazineAmmo, Mathf.Max(0, magazine));
            magazineAmmo += addedMagazine;
            reserveAmmo += Mathf.Max(0, reserve);
            WeaponStateChanged?.Invoke(this);
        }

        private void SpawnMuzzleFlash()
        {
            if (definition.muzzleFlashPrefab == null || muzzle == null)
                return;
            GameObject flash = ObjectPool.Instantiate(definition.muzzleFlashPrefab, muzzle.position, muzzle.rotation);
            flash.transform.localScale = Random.Range(0.9f, 1.15f) * Vector3.one;
        }

        private void SpawnImpact(Vector3 position, Vector3 normal)
        {
            if (definition.impactPrefab == null)
                return;
            GameObject impact = ObjectPool.Instantiate(definition.impactPrefab, position, Quaternion.LookRotation(normal));
            impact.transform.localScale = Random.Range(0.8f, 1.2f) * Vector3.one;
        }

        private void EjectShell()
        {
            if (definition.shellPrefab == null || definition.shellEjectionPoint == null)
                return;
            GameObject shell = ObjectPool.Instantiate(definition.shellPrefab,
                definition.shellEjectionPoint.position, definition.shellEjectionPoint.rotation);
            Rigidbody body = shell.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.velocity = definition.shellEjectionPoint.right * 1.2f + definition.shellEjectionPoint.up * Random.Range(1.5f, 2.5f) + Vector3.up * 1.5f;
                body.angularVelocity = Random.insideUnitSphere * 15f;
            }
        }

        private void PlayOneShot(AudioClip clip, float volume = 1f)
        {
            if (clip == null)
                return;
            AudioSource source = GetComponent<AudioSource>();
            if (source == null)
                source = gameObject.AddComponent<AudioSource>();
            source.PlayOneShot(clip, volume);
        }

        private void OnDrawGizmosSelected()
        {
            if (muzzle == null)
                return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(muzzle.position, muzzle.position + muzzle.forward * 2f);
        }
    }
}
