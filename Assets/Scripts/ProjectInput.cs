using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>Input System access for the project's keyboard/mouse sample controls.</summary>
public static class ProjectInput
{
    private static readonly Dictionary<KeyCode, Key> keys = new Dictionary<KeyCode, Key>();
    public static Vector3 mousePosition => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;

    private static ButtonControl KeyButton(KeyCode code)
    {
        if (code >= KeyCode.Mouse0 && code <= KeyCode.Mouse4) return MouseButton(code - KeyCode.Mouse0);
        if (Keyboard.current == null || code == KeyCode.None) return null;
        if (!keys.TryGetValue(code, out Key key))
        {
            string name = code.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) name = "Digit" + name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) name = "Numpad" + name.Substring(6);
            switch (code)
            {
                case KeyCode.Return: name = "Enter"; break;
                case KeyCode.LeftControl: name = "LeftCtrl"; break;
                case KeyCode.RightControl: name = "RightCtrl"; break;
                case KeyCode.LeftCommand: name = "LeftMeta"; break;
                case KeyCode.RightCommand: name = "RightMeta"; break;
                case KeyCode.Numlock: name = "NumLock"; break;
                case KeyCode.Print: name = "PrintScreen"; break;
            }
            Enum.TryParse(name, true, out key);
            keys[code] = key;
        }
        return key == Key.None ? null : Keyboard.current[key];
    }

    private static ButtonControl MouseButton(int button)
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return null;
        switch (button)
        {
            case 0: return mouse.leftButton;
            case 1: return mouse.rightButton;
            case 2: return mouse.middleButton;
            case 3: return mouse.forwardButton;
            case 4: return mouse.backButton;
            default: return null;
        }
    }

    public static bool GetKey(KeyCode key) => KeyButton(key)?.isPressed == true;
    public static bool GetKeyDown(KeyCode key) => KeyButton(key)?.wasPressedThisFrame == true;
    public static bool GetKeyUp(KeyCode key) => KeyButton(key)?.wasReleasedThisFrame == true;
    public static bool GetKeyDown(string name)
    {
        if (name == "up") return GetKeyDown(KeyCode.UpArrow);
        if (name == "down") return GetKeyDown(KeyCode.DownArrow);
        return Enum.TryParse(name, true, out KeyCode key) && GetKeyDown(key);
    }
    public static bool GetMouseButton(int button) => MouseButton(button)?.isPressed == true;
    public static bool GetMouseButtonDown(int button) => MouseButton(button)?.wasPressedThisFrame == true;

    private static bool Pressed(ButtonControl button, bool down) => button != null && (down ? button.wasPressedThisFrame : button.isPressed);
    private static bool Button(string name, bool down)
    {
        Gamepad pad = Gamepad.current;
        switch (name)
        {
            case "Jump": return Pressed(KeyButton(KeyCode.Space), down) || Pressed(pad?.buttonNorth, down);
            case "Fire1": return Pressed(KeyButton(KeyCode.LeftControl), down) || Pressed(MouseButton(0), down) || Pressed(pad?.buttonSouth, down);
            case "Fire2": return Pressed(KeyButton(KeyCode.LeftAlt), down) || Pressed(MouseButton(1), down) || Pressed(pad?.buttonEast, down);
            case "Fire3": return Pressed(KeyButton(KeyCode.LeftShift), down) || Pressed(MouseButton(2), down) || Pressed(pad?.buttonWest, down);
            case "Submit": return Pressed(KeyButton(KeyCode.Return), down) || Pressed(KeyButton(KeyCode.KeypadEnter), down) || Pressed(KeyButton(KeyCode.Space), down) || Pressed(pad?.buttonSouth, down);
            case "Cancel": return Pressed(KeyButton(KeyCode.Escape), down) || Pressed(pad?.buttonEast, down);
            default: return false;
        }
    }
    public static bool GetButton(string name) => Button(name, false);
    public static bool GetButtonDown(string name) => Button(name, true);

    public static float GetAxis(string name)
    {
        Vector2 stick = Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
        switch (name)
        {
            case "Horizontal": return Mathf.Clamp((GetKey(KeyCode.D) || GetKey(KeyCode.RightArrow) ? 1f : 0f) - (GetKey(KeyCode.A) || GetKey(KeyCode.LeftArrow) ? 1f : 0f) + stick.x, -1f, 1f);
            case "Vertical": return Mathf.Clamp((GetKey(KeyCode.W) || GetKey(KeyCode.UpArrow) ? 1f : 0f) - (GetKey(KeyCode.S) || GetKey(KeyCode.DownArrow) ? 1f : 0f) + stick.y, -1f, 1f);
            case "Mouse ScrollWheel": return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 1200f : 0f;
            case "Mouse X": return Mouse.current != null ? Mouse.current.delta.ReadValue().x * 0.1f : 0f;
            case "Mouse Y": return Mouse.current != null ? Mouse.current.delta.ReadValue().y * 0.1f : 0f;
            default: return GetButton(name) ? 1f : 0f;
        }
    }
}
