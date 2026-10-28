using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.WorldScaleRuntime
{
    /// <summary>Close inspection of small specimens without pointer capture or gameplay collision.</summary>
    public sealed class WorkshopCamera : MonoBehaviour
    {
        public const float FieldOfView = 45;
        Vector3 target, framedTarget;
        float distance = 24, framedDistance = 24, yaw = -32, pitch = 28;

        public void Frame(Vector3 center, float viewDistance)
        {
            framedTarget = center; framedDistance = Mathf.Clamp(viewDistance, .35f, 80);
            ResetFrame(); Apply();
        }

        void LateUpdate()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                if (mouse.rightButton.isPressed)
                {
                    yaw += delta.x * .18f;
                    pitch = Mathf.Clamp(pitch - delta.y * .18f, -12, 84);
                }
                distance = Mathf.Clamp(distance * Mathf.Exp(-mouse.scroll.ReadValue().y * .001f), .35f, 80);
                if (mouse.middleButton.isPressed)
                {
                    Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
                    float metersPerPixel = distance * (float)(2 * System.Math.Tan(FieldOfView * System.Math.PI / 360)) / Mathf.Max(1, Screen.height);
                    target -= (rotation * Vector3.right * delta.x + rotation * Vector3.up * delta.y) * metersPerPixel;
                }
            }
            if (Keyboard.current != null && Keyboard.current.homeKey.wasPressedThisFrame) ResetFrame();
            Apply();
        }

        void ResetFrame() { target = framedTarget; distance = framedDistance; yaw = -32; pitch = 28; }
        void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = target - rotation * Vector3.forward * distance;
            transform.rotation = rotation;
        }
    }
}
