using UnityEngine;

namespace Vanguard.InputSystem
{
    public enum TouchControlAction
    {
        Move,
        Look,
        Fire,
        Aim,
        Jump,
        Crouch,
        Slide,
        Reload,
        SwitchWeapon,
        Melee
    }

    public struct TouchActionState
    {
        public bool Held;
        public bool Pressed;
        public bool Released;
        public Vector2 Delta;
        public float Magnitude;
        public int TouchId;
    }

    public sealed class TouchActionMap : MonoBehaviour
    {
        [System.Serializable]
        public sealed class TouchZone
        {
            public TouchControlAction Action;
            public RectTransform Zone;
            public bool ConsumeTouch = true;
        }

        [SerializeField] private Camera inputCamera;
        [SerializeField] private float joystickDeadZone = 0.12f;
        [SerializeField] private TouchZone[] touchZones;
        [SerializeField] private KeyCode fireKey = KeyCode.Mouse0;
        [SerializeField] private KeyCode aimKey = KeyCode.Mouse1;
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;
        [SerializeField] private KeyCode slideKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode reloadKey = KeyCode.R;
        [SerializeField] private KeyCode switchWeaponKey = KeyCode.Q;
        [SerializeField] private KeyCode meleeKey = KeyCode.F;

        private readonly System.Collections.Generic.Dictionary<TouchControlAction, TouchActionState> _states =
            new System.Collections.Generic.Dictionary<TouchControlAction, TouchActionState>();
        private readonly System.Collections.Generic.Dictionary<int, TouchZone> _activeTouches =
            new System.Collections.Generic.Dictionary<int, TouchZone>();
        private Vector2 _lastLookPosition;
        private bool _hasLookPosition;

        public TouchActionState GetState(TouchControlAction action)
        {
            return _states.TryGetValue(action, out TouchActionState state) ? state : default;
        }

        public bool IsPressed(TouchControlAction action) => GetState(action).Pressed;
        public bool IsHeld(TouchControlAction action) => GetState(action).Held;

        public Vector2 MoveVector => GetState(TouchControlAction.Move).Delta;
        public Vector2 LookDelta => GetState(TouchControlAction.Look).Delta;

        private void Awake()
        {
            if (inputCamera == null)
                inputCamera = Camera.main;

            foreach (TouchControlAction action in System.Enum.GetValues(typeof(TouchControlAction)))
                _states[action] = default;
        }

        private void Update()
        {
            foreach (TouchControlAction action in _states.Keys)
            {
                TouchActionState state = _states[action];
                state.Pressed = false;
                state.Released = false;
                state.Delta = Vector2.zero;
                state.Magnitude = 0f;
                state.TouchId = -1;
                _states[action] = state;
            }

            ReadKeyboard();
            ReadTouches();
        }

        private void ReadKeyboard()
        {
            SetDigital(TouchControlAction.Fire, fireKey);
            SetDigital(TouchControlAction.Aim, aimKey);
            SetDigital(TouchControlAction.Jump, jumpKey);
            SetDigital(TouchControlAction.Crouch, crouchKey);
            SetDigital(TouchControlAction.Slide, slideKey);
            SetDigital(TouchControlAction.Reload, reloadKey);
            SetDigital(TouchControlAction.SwitchWeapon, switchWeaponKey);
            SetDigital(TouchControlAction.Melee, meleeKey);

            Vector2 keyboardMove = Vector2.zero;
            if (Input.GetKey(KeyCode.W)) keyboardMove.y += 1f;
            if (Input.GetKey(KeyCode.S)) keyboardMove.y -= 1f;
            if (Input.GetKey(KeyCode.A)) keyboardMove.x -= 1f;
            if (Input.GetKey(KeyCode.D)) keyboardMove.x += 1f;
            SetAnalog(TouchControlAction.Move, keyboardMove.normalized, keyboardMove.magnitude > 0f ? 1f : 0f, -1);

            Vector2 mouseDelta = new Vector2(Input.mouseX, Input.mouseY);
            if (mouseDelta.sqrMagnitude > 0f)
                AddDelta(TouchControlAction.Look, mouseDelta, -1);
        }

        private void SetDigital(TouchControlAction action, KeyCode key)
        {
            TouchActionState state = GetState(action);
            bool held = Input.GetKey(key);
            bool pressed = Input.GetKeyDown(key);
            bool released = Input.GetKeyUp(key);
            state.Held |= held;
            state.Pressed |= pressed;
            state.Released |= released;
            if (held || pressed)
            {
                state.TouchId = -2;
                state.Magnitude = 1f;
            }
            _states[action] = state;
        }

        private void SetAnalog(TouchControlAction action, Vector2 delta, float magnitude, int touchId)
        {
            TouchActionState state = GetState(action);
            state.Held = magnitude > joystickDeadZone;
            state.Pressed |= !state.Held && magnitude > 0f;
            state.Delta += delta;
            state.Magnitude = Mathf.Max(state.Magnitude, magnitude);
            state.TouchId = touchId;
            _states[action] = state;
        }

        private void AddDelta(TouchControlAction action, Vector2 delta, int touchId)
        {
            TouchActionState state = GetState(action);
            state.Held = true;
            state.Delta += delta;
            state.Magnitude = Mathf.Max(state.Magnitude, delta.magnitude);
            state.TouchId = touchId;
            _states[action] = state;
        }

        private void ReadTouches()
        {
            if (Input.touchCount <= 0)
            {
                _activeTouches.Clear();
                _hasLookPosition = false;
                return;
            }

            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                TouchZone zone = null;

                if (_activeTouches.TryGetValue(touch.fingerId, out TouchZone activeZone))
                {
                    zone = activeZone;
                }
                else
                {
                    zone = FindZone(touch.position);
                    if (zone != null && zone.ConsumeTouch)
                        _activeTouches[touch.fingerId] = zone;
                }

                if (zone == null)
                    continue;

                Vector2 delta = touch.deltaPosition;
                float magnitude = zone.Action == TouchControlAction.Move
                    ? GetJoystickMagnitude(touch, zone)
                    : 1f;

                if (touch.phase == TouchPhase.Began)
                {
                    TouchActionState state = GetState(zone.Action);
                    state.Pressed = true;
                    state.Held = true;
                    state.TouchId = touch.fingerId;
                    _states[zone.Action] = state;
                    if (zone.Action == TouchControlAction.Look)
                    {
                        _lastLookPosition = touch.position;
                        _hasLookPosition = true;
                    }
                }

                if (zone.Action == TouchControlAction.Look && _hasLookPosition)
                    delta = touch.position - _lastLookPosition;

                if (delta.sqrMagnitude > 0f || magnitude > joystickDeadZone)
                    SetAnalog(zone.Action, delta, magnitude, touch.fingerId);

                if (zone.Action == TouchControlAction.Look)
                    _lastLookPosition = touch.position;

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    TouchActionState released = GetState(zone.Action);
                    released.Released = true;
                    released.Held = false;
                    released.TouchId = -1;
                    _states[zone.Action] = released;
                    _activeTouches.Remove(touch.fingerId);
                    if (zone.Action == TouchControlAction.Look)
                        _hasLookPosition = false;
                }
            }
        }

        private TouchZone FindZone(Vector2 screenPosition)
        {
            if (inputCamera == null)
                return null;

            foreach (TouchZone zone in touchZones)
            {
                if (zone.Zone == null || !zone.Zone.gameObject.activeInHierarchy)
                    continue;

                if (RectTransformUtility.RectangleContainsScreenPoint(zone.Zone, screenPosition, inputCamera))
                    return zone;
            }

            return null;
        }

        private float GetJoystickMagnitude(Touch touch, TouchZone zone)
        {
            if (zone.Zone == null || inputCamera == null)
                return 0f;

            RectTransform rect = zone.Zone;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect,
                touch.position,
                inputCamera,
                out Vector2 localPoint);

            Vector2 size = rect.rect.size;
            Vector2 normalized = new Vector2(
                Mathf.Clamp(localPoint.x / Mathf.Max(size.x * 0.5f, 1f), -1f, 1f),
                Mathf.Clamp(localPoint.y / Mathf.Max(size.y * 0.5f, 1f), -1f, 1f));
            return normalized.magnitude;
        }
    }
}
