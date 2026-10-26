using System;
using UnityEngine;
using UnityEngine.Scripting;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace KineticCore.Input
{
    /// <summary>
    /// Keyboard / mouse shim that works whether "Active Input Handling" is set to
    /// Input System Package (New), Input Manager (Old), or Both.
    /// </summary>
    [Preserve]
    public static class KInput
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        private static Vector3 _lastLegacyMousePos;
        private static int _lastLegacyMouseFrame = -1;
        private static Vector2 _legacyMouseDelta;
#endif

        public static bool GetKey(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                Key key = ToKey(code);
                if (key != Key.None && kb[key].isPressed) return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (UnityEngine.Input.GetKey(code)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        public static bool GetKeyDown(KeyCode code)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                Key key = ToKey(code);
                if (key != Key.None && kb[key].wasPressedThisFrame) return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (UnityEngine.Input.GetKeyDown(code)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        /// <summary>0 = left, 1 = right, 2 = middle.</summary>
        public static bool GetMouseButton(int button)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null)
            {
                switch (button)
                {
                    case 0: if (m.leftButton.isPressed) return true; break;
                    case 1: if (m.rightButton.isPressed) return true; break;
                    case 2: if (m.middleButton.isPressed) return true; break;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (UnityEngine.Input.GetMouseButton(button)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        public static bool GetMouseButtonDown(int button)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null)
            {
                switch (button)
                {
                    case 0: if (m.leftButton.wasPressedThisFrame) return true; break;
                    case 1: if (m.rightButton.wasPressedThisFrame) return true; break;
                    case 2: if (m.middleButton.wasPressedThisFrame) return true; break;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { if (UnityEngine.Input.GetMouseButtonDown(button)) return true; } catch (InvalidOperationException) { }
#endif
            return false;
        }

        public static Vector2 MousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null) return m.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
#endif
            return Vector2.zero;
        }

        /// <summary>Mouse movement this frame in pixels.</summary>
        public static Vector2 MouseDelta()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null) return m.delta.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try
            {
                if (_lastLegacyMouseFrame != Time.frameCount)
                {
                    Vector3 p = UnityEngine.Input.mousePosition;
                    _legacyMouseDelta = _lastLegacyMouseFrame < 0 ? Vector2.zero : (Vector2)(p - _lastLegacyMousePos);
                    _lastLegacyMousePos = p;
                    _lastLegacyMouseFrame = Time.frameCount;
                }
                return _legacyMouseDelta;
            }
            catch (InvalidOperationException) { }
#endif
            return Vector2.zero;
        }

        /// <summary>Scroll wheel this frame, normalised to roughly +/-1 per notch.</summary>
        public static float ScrollDelta()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse m = Mouse.current;
            if (m != null)
            {
                float y = m.scroll.ReadValue().y;
                return Mathf.Abs(y) > 10f ? y / 120f : y;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            try { return UnityEngine.Input.mouseScrollDelta.y; } catch (InvalidOperationException) { }
#endif
            return 0f;
        }

#if ENABLE_INPUT_SYSTEM
        private static Key ToKey(KeyCode code)
        {
            switch (code)
            {
                case KeyCode.W: return Key.W;
                case KeyCode.A: return Key.A;
                case KeyCode.S: return Key.S;
                case KeyCode.D: return Key.D;
                case KeyCode.Q: return Key.Q;
                case KeyCode.E: return Key.E;
                case KeyCode.G: return Key.G;
                case KeyCode.H: return Key.H;
                case KeyCode.U: return Key.U;
                case KeyCode.P: return Key.P;
                case KeyCode.R: return Key.R;
                case KeyCode.O: return Key.O;
                case KeyCode.Space: return Key.Space;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                default: return Key.None;
            }
        }
#endif
    }
}
