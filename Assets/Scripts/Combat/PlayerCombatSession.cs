using UnityEngine;

namespace Vanguard.Combat
{
    public sealed class PlayerCombatSession : MonoBehaviour
    {
        [SerializeField] private Weapon[] weapons;
        [SerializeField] private FirstPerson.Player.FirstPersonController controller;
        [SerializeField] private Transform weaponRoot;
        [SerializeField] private float meleeRange = 2.1f;
        [SerializeField] private float meleeDamage = 75f;
        [SerializeField] private LayerMask targetMask = ~0;

        private Weapon _activeWeapon;
        private int _activeIndex;
        private Weapon _pendingWeapon;
        private float _switchTime;
        private bool _meleeOnCooldown;

        public Weapon ActiveWeapon => _activeWeapon;
        public event System.Action<Weapon, RaycastHit, DamageLocation> HitRegistered;
        public event System.Action<Weapon> ReloadStarted;
        public event System.Action<Weapon> ReloadCompleted;

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<FirstPerson.Player.FirstPersonController>();
            if (weaponRoot == null)
                weaponRoot = transform.Find("WeaponRoot") ?? transform;

            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon weapon = weapons[i];
                weapon.gameObject.SetActive(i == 0);
                weapon.HitRegistered += HandleHit;
                weapon.ReloadStarted += value => ReloadStarted?.Invoke(value);
                weapon.ReloadCompleted += value => ReloadCompleted?.Invoke(value);
            }

            Equip(weapons.Length > 0 ? weapons[0] : null, true);
        }

        private void Update()
        {
            if (_pendingWeapon != null && Time.time >= _switchTime)
                Equip(_pendingWeapon, false);

            if (controller != null && controller.IsReloadPressed() && _activeWeapon != null)
                _activeWeapon.StartReload();

            if (controller != null && controller.IsSwitchWeaponPressed())
                CycleWeapon();

            if (controller != null && controller.IsMeleePressed() && !_meleeOnCooldown)
                PerformMelee();
        }

        private void Equip(Weapon weapon, bool instant)
        {
            if (_activeWeapon == weapon)
                return;

            if (_activeWeapon != null)
                _activeWeapon.gameObject.SetActive(false);
            _activeWeapon = weapon;
            _activeIndex = System.Array.IndexOf(weapons, weapon);
            _switchTime = Time.time + (instant ? 0f : weapon.Definition.equipTime);
            if (weapon != null)
            {
                weapon.transform.SetParent(weaponRoot, false);
                weapon.gameObject.SetActive(true);
            }
        }

        private void CycleWeapon()
        {
            if (weapons == null || weapons.Length <= 1)
                return;
            int next = (_activeIndex + 1) % weapons.Length;
            _pendingWeapon = weapons[next];
            _switchTime = Time.time + 0.22f;
            if (_activeWeapon != null)
                _activeWeapon.gameObject.SetActive(false);
        }

        private void PerformMelee()
        {
            _meleeOnCooldown = true;
            Invoke(nameof(ResetMelee), 0.55f);
            Vector3 origin = controller != null ? controller.CameraHolder.position : transform.position;
            if (!Physics.Raycast(origin, transform.forward, out RaycastHit hit, meleeRange, targetMask, QueryTriggerInteraction.Ignore))
                return;

            Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
            if (damageable == null)
                return;
            damageable.ApplyDamage(new DamageEvent(meleeDamage, DamageLocation.Other, gameObject, gameObject,
                hit.point, transform.forward, false));
            HandleHit(null, hit, DamageLocation.Other);
        }

        private void ResetMelee()
        {
            _meleeOnCooldown = false;
        }

        private void HandleHit(Weapon weapon, RaycastHit hit, DamageLocation location)
        {
            HitRegistered?.Invoke(weapon, hit, location);
        }

        private void OnDisable()
        {
            foreach (Weapon weapon in weapons)
            {
                if (weapon == null)
                    continue;
                weapon.HitRegistered -= HandleHit;
            }
        }
    }
}
