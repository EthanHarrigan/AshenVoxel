using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Authoring
{
    /// <summary>Simple inspection camera. It does not use gameplay collision or capture the pointer.</summary>
    public sealed class WorkshopOrbitCamera : MonoBehaviour
    {
        Vector3 target, framedTarget;
        internal const float FieldOfView = 50;
        float distance = 30, framedDistance = 30, yaw = -35, pitch = 16;
        public void Frame(Vector3 center, float radius)
        {
            framedTarget = center; framedDistance = Mathf.Clamp(radius, 3, 700); ResetFrame(); Apply();
        }
        void LateUpdate()
        {
            var mouse = Mouse.current; var keyboard = Keyboard.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * .18f; pitch = Mathf.Clamp(pitch - delta.y * .18f, -15, 82);
                }
                distance = Mathf.Clamp(distance * Mathf.Exp(-mouse.scroll.ReadValue().y * .001f), 1.5f, 700);
                if (mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
                    // Match the 50-degree inspection camera: a pixel spans more meters when distant.
                    float metersPerPixel = distance * (float)(2 * System.Math.Tan(FieldOfView * System.Math.PI / 360))
                        / Mathf.Max(1, Screen.height);
                    target -= (rotation * Vector3.right * delta.x + rotation * Vector3.up * delta.y) * metersPerPixel;
                }
            }
            if (keyboard != null && keyboard.homeKey.wasPressedThisFrame)
                ResetFrame();
            Apply();
        }
        void ResetFrame()
        { target = framedTarget; distance = framedDistance; yaw = -35; pitch = 16; }
        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
