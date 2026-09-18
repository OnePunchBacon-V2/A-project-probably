using UnityEngine;

namespace Vanguard.Bots
{
    public enum BotState
    {
        Patrol,
        Investigate,
        Combat,
        Reload,
        Retreat
    }

    public sealed class BotController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private Match.MatchTeam team = Match.MatchTeam.Bravo;
        [SerializeField] private string botName = "Bot";
        [SerializeField] private float detectionRange = 28f;
        [SerializeField] private float hearingRange = 38f;
        [SerializeField] private float reactionDelay = 0.28f;
        [SerializeField] private float aimErrorDegrees = 2.2f;
        [SerializeField] private float engagementDistance = 24f;
        [SerializeField] private float strafeChangeInterval = 1.2f;
        [SerializeField] private float patrolChangeInterval = 3f;
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private Transform localTarget;
        [SerializeField] private Weapon weapon;
        [SerializeField] private LayerMask obstacleMask = ~0;
        [SerializeField] private float reloadAtAmmo = 8;

        private CharacterController _controller;
        private Damageable _damageable;
        private Match.MatchController _match;
        private Match.MatchPlayer _profile;
        private Vector3 _moveDirection;
        private Vector3 _targetPosition;
        private Transform _knownTarget;
        private Damageable _knownTargetDamageable;
        private BotState _state = BotState.Patrol;
        private float _stateTimer;
        private float _reactionAt;
        private float _lastSeenAt = -99f;
        private float _lastKnownSoundAt = -99f;
        private Vector3 _lastKnownPosition;
        private float _strafeDirection = 1f;
        private float _patrolIndex;
        private bool _hasLineOfSight;
        private bool _dead;

        public Match.MatchTeam Team => team;
        public bool IsAlive => !_dead && _damageable != null && _damageable.IsAlive;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _damageable = GetComponent<Damageable>();
            _match = FindObjectOfType<Match.MatchController>();
            _profile = new Match.MatchPlayer
            {
                Id = gameObject.name,
                DisplayName = botName,
                Team = team
            };

            if (_damageable != null)
            {
                _damageable.Damaged += HandleDamaged;
                _damageable.Killed += HandleKilled;
            }
            if (weapon != null)
            {
                weapon.HitRegistered += HandleWeaponHit;
                weapon.ReloadStarted += _ => SetState(BotState.Reload);
                weapon.ReloadCompleted += _ => SetState(BotState.Combat);
            }
        }

        private void OnEnable()
        {
            _dead = false;
            if (_damageable != null)
                _damageable.Heal(_damageable.MaxHealth);
            SetState(BotState.Patrol);
        }

        private void Update()
        {
            if (_dead || _match == null || _match.Phase != Match.MatchPhase.Playing)
                return;
            UpdateTarget();
            UpdateState();
            UpdateMovement();
        }

        private void UpdateTarget()
        {
            if (_knownTarget != null)
                _knownTargetDamageable = _knownTarget.GetComponent<Damageable>();
            if (_knownTarget != null && _knownTargetDamageable != null && _knownTargetDamageable.IsAlive &&
                CanHear(_knownTarget.position) && (HasLineOfSight(_knownTarget.position) ||
                Vector3.Distance(transform.position, _knownTarget.position) < 5f))
            {
                _lastSeenAt = Time.time;
                _lastKnownPosition = _knownTarget.position;
                if (Time.time >= _reactionAt)
                    SetState(BotState.Combat);
            }
            else if (_knownTarget != null && Time.time - _lastSeenAt > 2.5f)
            {
                _knownTarget = null;
                SetState(BotState.Investigate);
            }
        }

        private void UpdateState()
        {
            _stateTimer -= Time.deltaTime;
            if (_state == BotState.Combat)
            {
                if (_knownTarget == null)
                {
                    SetState(BotState.Investigate);
                    return;
                }

                FaceTarget();
                if (weapon != null && !_damageable.IsAlive)
                    return;
                if (weapon != null && weapon.MagazineAmmo <= reloadAtAmmo && weapon.ReserveAmmo > 0)
                    weapon.StartReload();
                if (_hasLineOfSight && weapon != null && Time.time >= _reactionAt)
                    SimulateFire();
                if (_stateTimer <= 0f)
                {
                    _strafeDirection *= -1f;
                    _stateTimer = strafeChangeInterval * Random.Range(0.7f, 1.5f);
                }
            }
            else if (_state == BotState.Patrol)
            {
                UpdatePatrolTarget();
                if (_stateTimer <= 0f)
                {
                    _patrolIndex = (_patrolIndex + 1f) % Mathf.Max(1, patrolPoints.Length);
                    _stateTimer = patrolChangeInterval * Random.Range(0.8f, 1.4f);
                }
            }
            else if (_state == BotState.Investigate)
            {
                _targetPosition = _lastKnownPosition;
                if (Vector3.Distance(transform.position, _targetPosition) < 1.5f || _stateTimer <= 0f)
                    SetState(BotState.Patrol);
            }
            else if (_state == BotState.Retreat)
            {
                if (_knownTarget != null)
                    _targetPosition = transform.position - (_knownTarget.position - transform.position).normalized * 8f;
                if (_stateTimer <= 0f)
                    SetState(BotState.Combat);
            }
        }

        private void UpdatePatrolTarget()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
                return;
            _targetPosition = patrolPoints[Mathf.FloorToInt(_patrolIndex) % patrolPoints.Length].position;
        }

        private void UpdateMovement()
        {
            Vector3 desired = Vector3.zero;
            float speed = 3.5f;
            if (_state == BotState.Combat && _knownTarget != null)
            {
                Vector3 toTarget = _knownTarget.position - transform.position;
                toTarget.y = 0f;
                float distance = toTarget.magnitude;
                if (distance > engagementDistance * 0.7f)
                    desired = toTarget.normalized;
                else if (distance < engagementDistance * 0.35f)
                    desired = -toTarget.normalized;
                else
                    desired = Vector3.Cross(toTarget.normalized, Vector3.up) * _strafeDirection;
                speed = distance > engagementDistance ? 5.4f : 3.2f;
            }
            else if (_state == BotState.Investigate || _state == BotState.Patrol)
            {
                Vector3 toTarget = _targetPosition - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.5f)
                    desired = toTarget.normalized;
                speed = 3.2f;
            }
            else if (_state == BotState.Retreat)
            {
                Vector3 toTarget = _targetPosition - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.5f)
                    desired = toTarget.normalized;
                speed = 5.2f;
            }

            desired = Vector3.ClampMagnitude(desired, 1f);
            Vector3 velocity = desired * speed;
            velocity.y = _controller.isGrounded ? -2f : velocity.y - 18f * Time.deltaTime;
            _controller.Move(velocity * Time.deltaTime);
        }

        private void FaceTarget()
        {
            if (_knownTarget == null)
                return;
            Vector3 direction = _knownTarget.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.01f)
                return;
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 10f * Time.deltaTime);
        }

        private void SimulateFire()
        {
            if (weapon == null || !_hasLineOfSight)
                return;
            Vector3 targetPosition = _knownTarget.position + Vector3.up * 1.25f;
            Vector3 direction = (targetPosition - weapon.transform.position).normalized;
            direction = Quaternion.AngleAxis(Random.Range(-aimErrorDegrees, aimErrorDegrees), Vector3.up) * direction;
            direction = Quaternion.AngleAxis(Random.Range(-aimErrorDegrees, aimErrorDegrees), Vector3.right) * direction;
            if (Physics.Raycast(weapon.transform.position, direction, out RaycastHit hit, 80f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
                if (damageable != null && damageable.IsAlive)
                {
                    DamageLocation location = Damageable.GetLocationFromCollider(hit.collider);
                    float damage = weapon.Definition.GetDamage(hit.distance);
                    if (location == DamageLocation.Head)
                        damage *= weapon.Definition.headMultiplier;
                    else if (location == DamageLocation.LeftArm || location == DamageLocation.RightArm ||
                             location == DamageLocation.LeftLeg || location == DamageLocation.RightLeg)
                        damage *= weapon.Definition.limbMultiplier;
                    damageable.ApplyDamage(new DamageEvent(damage, location, gameObject, gameObject,
                        hit.point, direction, false));
                    _match.AwardKill(_profile, GetPlayerProfile(damageable), location, hit.distance);
                }
            }
        }

        private Match.MatchPlayer GetPlayerProfile(Damageable damageable)
        {
            Match.MatchController match = FindObjectOfType<Match.MatchController>();
            if (match != null && match.LocalPlayer != null && damageable.transform.root == match.transform)
                return match.LocalPlayer;
            return null;
        }

        private bool HasLineOfSight(Vector3 position)
        {
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 target = position + Vector3.up * 1.2f;
            if (!Physics.Linecast(origin, target, out RaycastHit hit, obstacleMask))
            {
                _hasLineOfSight = true;
                return true;
            }
            _hasLineOfSight = hit.collider.GetComponentInParent<Damageable>() != null;
            return _hasLineOfSight;
        }

        private bool CanHear(Vector3 position)
        {
            return Vector3.Distance(transform.position, position) <= hearingRange;
        }

        private void HandleDamaged(DamageEvent damage)
        {
            if (damage.Source == null)
                return;
            _lastKnownSoundAt = Time.time;
            _lastKnownPosition = damage.Position;
            if (localTarget != null && _knownTarget == null)
                _knownTarget = localTarget;
            _reactionAt = Time.time + reactionDelay;
            if (_damageable.Health < 35f)
                SetState(BotState.Retreat);
            else
                SetState(BotState.Combat);
        }

        private void HandleWeaponHit(Weapon source, RaycastHit hit, DamageLocation location)
        {
            if (hit.collider == null)
                return;
            Damageable damageable = hit.collider.GetComponentInParent<Damageable>();
            if (damageable != null && damageable.IsAlive && damageable.transform != transform)
                _lastKnownSoundAt = Time.time;
        }

        private void HandleKilled(GameObject source)
        {
            _dead = true;
            gameObject.SetActive(false);
        }

        private void SetState(BotState state)
        {
            _state = state;
            _stateTimer = state == BotState.Combat ? strafeChangeInterval : patrolChangeInterval;
        }

        private void OnDisable()
        {
            if (_damageable != null)
            {
                _damageable.Damaged -= HandleDamaged;
                _damageable.Killed -= HandleKilled;
            }
            if (weapon != null)
            {
                weapon.HitRegistered -= HandleWeaponHit;
                weapon.ReloadStarted -= _ => SetState(BotState.Reload);
                weapon.ReloadCompleted -= _ => SetState(BotState.Combat);
            }
        }
    }
}
