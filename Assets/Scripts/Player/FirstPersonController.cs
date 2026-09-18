using UnityEngine;

namespace Vanguard.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("Camera")]
        [SerializeField] private Transform cameraHolder;
        [SerializeField] private float baseFov = 75f;
        [SerializeField] private float adsFov = 55f;
        [SerializeField] private float lookSensitivity = 0.0022f;
        [SerializeField] private float adsLookSensitivityMultiplier = 0.75f;
        [SerializeField] private bool invertY;
        [SerializeField] private float minPitch = -89f;
        [SerializeField] private float maxPitch = 89f;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 4.4f;
        [SerializeField] private float sprintSpeed = 7.2f;
        [SerializeField] private float tacticalSprintSpeed = 8.8f;
        [SerializeField] private float crouchSpeed = 2.2f;
        [SerializeField] private float slideSpeed = 8.5f;
        [SerializeField] private float slideDuration = 0.62f;
        [SerializeField] private float slideFriction = 5.5f;
        [SerializeField] private float acceleration = 18f;
        [SerializeField] private float airAcceleration = 7f;
        [SerializeField] private float gravity = 22f;
        [SerializeField] private float jumpSpeed = 5.2f;
        [SerializeField] private float stepHeight = 0.35f;
        [SerializeField] private float standingHeight = 1.72f;
        [SerializeField] private float crouchHeight = 1.12f;
        [SerializeField] private float mantleReach = 1.65f;
        [SerializeField] private float mantleMaxHeight = 1.35f;

        [Header("Camera Feel")]
        [SerializeField] private float headBobAmount = 0.025f;
        [SerializeField] private float headBobFrequency = 9f;
        [SerializeField] private float sprintBobMultiplier = 1.6f;
        [SerializeField] private float landDip = 0.08f;
        [SerializeField] private float recoilRecoverySpeed = 8f;
        [SerializeField] private float weaponSwayAmount = 0.025f;

        [Header("Input")]
        [SerializeField] private Vanguard.InputSystem.TouchActionMap inputMap;

        private CharacterController _controller;
        private Vector3 _velocity;
        private Vector3 _slideVelocity;
        private Vector2 _rotation;
        private Vector2 _recoil;
        private float _targetHeight;
        private float _currentHeight;
        private float _slideTimer;
        private float _bobPhase;
        private float _landDipVelocity;
        private float _landDipAmount;
        private bool _wasGrounded;
        private bool _mantling;
        private Vector3 _mantleStart;
        private Vector3 _mantleEnd;
        private float _mantleTimer;
        private float _mantleDuration;

        public Transform CameraHolder => cameraHolder;
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsGrounded => _controller.isGrounded;
        public float CurrentFov { get; private set; } = 75f;
        public Vector2 Recoil => _recoil;
        public float HeadBobPhase => _bobPhase;
        public float LandDip => _landDipAmount;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (cameraHolder == null)
            {
                cameraHolder = transform.Find("CameraHolder");
                if (cameraHolder == null)
                {
                    GameObject holder = new GameObject("CameraHolder");
                    holder.transform.SetParent(transform, false);
                    cameraHolder = holder.transform;
                }
            }

            if (inputMap == null)
                inputMap = FindObjectOfType<Vanguard.InputSystem.TouchActionMap>();

            _controller.enableOverlapRecovery = true;
            _controller.stepOffset = stepHeight;
            _targetHeight = standingHeight;
            _currentHeight = standingHeight;
            SetControllerHeight(_currentHeight);
        }

        private void Update()
        {
            ReadLook();
            UpdateMovement();
            UpdateCameraFeel();
        }

        private void ReadLook()
        {
            Vector2 look = inputMap != null ? inputMap.LookDelta : Vector2.zero;
            float multiplier = IsAiming() ? adsLookSensitivityMultiplier : 1f;
            float yDirection = invertY ? -1f : 1f;
            _rotation.y += look.x * lookSensitivity * multiplier;
            _rotation.x -= look.y * lookSensitivity * multiplier * yDirection;
            _rotation.x = Mathf.Clamp(_rotation.x, minPitch, maxPitch);
            _rotation.y %= 360f;
            transform.localRotation = Quaternion.Euler(0f, _rotation.y, 0f);
        }

        private void UpdateMovement()
        {
            Vector2 move = inputMap != null ? inputMap.MoveVector : Vector2.zero;
            bool jumpPressed = inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.Jump);
            bool crouchHeld = inputMap != null && inputMap.IsHeld(Vanguard.InputSystem.TouchControlAction.Crouch);
            bool slidePressed = inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.Slide);
            bool sprintHeld = Mathf.Abs(move.y) > 0.75f && move.y < 0f;
            bool tacticalSprintHeld = sprintHeld && Mathf.Abs(move.x) < 0.25f;

            IsSliding = slidePressed && _controller.isGrounded && CurrentHorizontalSpeed() > walkSpeed * 0.75f;
            if (IsSliding)
            {
                _slideTimer = slideDuration;
                Vector3 forward = transform.forward;
                forward.y = 0f;
                _slideVelocity = forward.normalized * slideSpeed;
            }

            if (_slideTimer > 0f)
            {
                _slideTimer -= Time.deltaTime;
                if (_slideTimer <= 0f || !_controller.isGrounded)
                    _slideVelocity = Vector3.zero;
            }

            IsCrouching = crouchHeld || _slideTimer > 0f;
            _targetHeight = IsCrouching ? crouchHeight : standingHeight;
            _currentHeight = Mathf.MoveTowards(_currentHeight, _targetHeight, 5f * Time.deltaTime);
            SetControllerHeight(_currentHeight);

            Vector3 wishDirection = transform.right * move.x + transform.forward * move.y;
            wishDirection.y = 0f;
            if (wishDirection.sqrMagnitude > 1f)
                wishDirection.Normalize();

            float targetSpeed = IsSliding ? 0f :
                IsCrouching ? crouchSpeed :
                tacticalSprintHeld ? tacticalSprintSpeed :
                sprintHeld ? sprintSpeed : walkSpeed;
            IsSprinting = sprintHeld && !IsCrouching && move.y < -0.2f;

            Vector3 targetVelocity = wishDirection * targetSpeed;
            float accel = _controller.isGrounded ? acceleration : airAcceleration;
            _velocity.x = Mathf.MoveTowards(_velocity.x, targetVelocity.x, accel * Time.deltaTime);
            _velocity.z = Mathf.MoveTowards(_velocity.z, targetVelocity.z, accel * Time.deltaTime);

            if (_slideTimer > 0f && _controller.isGrounded)
            {
                _slideVelocity = Vector3.MoveTowards(_slideVelocity, Vector3.zero, slideFriction * Time.deltaTime);
                _velocity.x += _slideVelocity.x * Time.deltaTime * 10f;
                _velocity.z += _slideVelocity.z * Time.deltaTime * 10f;
            }

            if (jumpPressed && _controller.isGrounded && _slideTimer <= 0f && !_mantling)
            {
                if (!TryMantle())
                    _velocity.y = jumpSpeed;
            }
            else
                _velocity.y -= gravity * Time.deltaTime;

            Vector3 displacement = _velocity * Time.deltaTime;
            if (_mantling)
                displacement = UpdateMantle();
            else
                _controller.Move(displacement);

            _wasGrounded = _controller.isGrounded;
        }

        private bool TryMantle()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();
            if (!Physics.Raycast(transform.position + Vector3.up * 0.9f, forward, out RaycastHit hit, mantleReach,
                    LayerMask.GetMask("Default", "Environment")))
                return false;

            Vector3 topPoint = hit.point + Vector3.up * mantleMaxHeight;
            if (!Physics.Linecast(transform.position + Vector3.up, topPoint, out _,
                    LayerMask.GetMask("Default", "Environment")))
                return false;

            Vector3 groundPoint = topPoint + Vector3.down * 0.2f;
            if (!Physics.Raycast(groundPoint, Vector3.down, out RaycastHit groundHit, 0.35f,
                    LayerMask.GetMask("Default", "Environment")))
                return false;

            _mantleStart = transform.position;
            _mantleEnd = groundHit.point + Vector3.up * 0.05f;
            _mantleEnd.y = Mathf.Max(_mantleEnd.y, _mantleStart.y);
            _mantleDuration = Mathf.Clamp01(Vector3.Distance(_mantleStart, _mantleEnd) / 3.2f);
            _mantleTimer = 0f;
            _mantling = true;
            _velocity = Vector3.zero;
            return true;
        }

        private Vector3 UpdateMantle()
        {
            _mantleTimer += Time.deltaTime;
            float progress = Mathf.InverseLerp(0f, _mantleDuration, _mantleTimer);
            progress = progress * progress * (3f - 2f * progress);
            if (progress >= 1f)
            {
                _mantling = false;
                transform.position = _mantleEnd;
                return Vector3.zero;
            }

            transform.position = Vector3.Lerp(_mantleStart, _mantleEnd, progress);
            return Vector3.zero;
        }

        private void UpdateCameraFeel()
        {
            float targetFov = IsAiming() ? adsFov : baseFov;
            CurrentFov = Mathf.MoveTowards(CurrentFov, targetFov, 120f * Time.deltaTime);

            float speed = new Vector2(_velocity.x, _velocity.z).magnitude;
            float bobTarget = _controller.isGrounded && speed > 0.5f && !_mantling
                ? headBobAmount * Mathf.Clamp01(speed / sprintSpeed) * (IsSprinting ? sprintBobMultiplier : 1f)
                : 0f;
            _bobPhase += Time.deltaTime * headBobFrequency * Mathf.Clamp01(speed / walkSpeed);
            float bob = Mathf.Sin(_bobPhase * Mathf.PI * 2f) * bobTarget;

            if (!_wasGrounded && _controller.isGrounded && _velocity.y < -4f)
                _landDipVelocity = Mathf.Clamp(_velocity.y * -0.012f, 0.05f, landDip);

            _landDipAmount = Mathf.MoveTowards(_landDipAmount, 0f, 8f * Time.deltaTime);
            _landDipVelocity = Mathf.MoveTowards(_landDipVelocity, 0f, 10f * Time.deltaTime);
            _landDipAmount += _landDipVelocity * Time.deltaTime;

            Vector2 lookDelta = inputMap != null ? inputMap.LookDelta : Vector2.zero;
            _recoil = Vector2.MoveTowards(_recoil, Vector2.zero, recoilRecoverySpeed * Time.deltaTime);
            _recoil.x = Mathf.Clamp(_recoil.x + lookDelta.y * weaponSwayAmount, -0.08f, 0.08f);
            _recoil.y = Mathf.Clamp(_recoil.y + lookDelta.x * weaponSwayAmount, -0.08f, 0.08f);

            cameraHolder.localPosition = new Vector3(_recoil.y, bob - _landDipAmount, _recoil.x);
            cameraHolder.localRotation = Quaternion.Euler(-_recoil.x * 12f, -_recoil.y * 12f, 0f);
            Camera camera = cameraHolder.GetComponentInChildren<Camera>();
            if (camera != null)
                camera.fieldOfView = CurrentFov;
        }

        public void AddRecoil(Vector2 recoil)
        {
            _recoil += recoil;
            _recoil.x = Mathf.Clamp(_recoil.x, -0.18f, 0.18f);
            _recoil.y = Mathf.Clamp(_recoil.y, -0.18f, 0.18f);
        }

        public bool IsAiming()
        {
            return inputMap != null && inputMap.IsHeld(Vanguard.InputSystem.TouchControlAction.Aim);
        }

        public bool IsFireHeld()
        {
            return inputMap != null && inputMap.IsHeld(Vanguard.InputSystem.TouchControlAction.Fire);
        }

        public bool IsFirePressed()
        {
            return inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.Fire);
        }

        public bool IsReloadPressed()
        {
            return inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.Reload);
        }

        public bool IsSwitchWeaponPressed()
        {
            return inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.SwitchWeapon);
        }

        public bool IsMeleePressed()
        {
            return inputMap != null && inputMap.IsPressed(Vanguard.InputSystem.TouchControlAction.Melee);
        }

        public float CurrentHorizontalSpeed()
        {
            return new Vector2(_velocity.x, _velocity.z).magnitude;
        }

        private float CurrentSpeed()
        {
            return _velocity.magnitude;
        }

        private void SetControllerHeight(float height)
        {
            _controller.center = new Vector3(0f, height * 0.5f, 0f);
            _controller.height = Mathf.Max(0.2f, height);
        }
    }
}
