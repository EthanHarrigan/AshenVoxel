using AshenVoxel.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Runtime
{
    // Authoritative grid collision: terrain edits take effect before mesh/collider rebuilds.
    public sealed class GridWalker : MonoBehaviour
    {
        public VoxelWorld World;
        public Camera View;
        public bool Fly;
        [Tooltip("Enable to collide with Leaves, Fungus and Petal matter. Tools still target it in either mode.")]
        public bool CollideWithSoftFoliage;
        public bool Grounded { get; private set; }
        public float Speed = 3.2f;
        const float Radius = .24f, Height = 1.65f;
        float yaw, pitch, vertical;
        public void Spawn(Vector3 feet) { transform.position = feet; vertical = 0; Grounded = false; }
        void Update()
        {
            var k = Keyboard.current; var mouse = Mouse.current;
            if (k == null || World == null) return;
            if (k.escapeKey.wasPressedThisFrame) Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
            if (mouse != null && View != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue() * .095f;
                yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -86, 86);
                transform.rotation = Quaternion.Euler(0, yaw, 0);
                View.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
            }
            // Flight is an inspection tool: do not turn collision back on inside a wall or outside the domain.
            if (k.fKey.wasPressedThisFrame && (!Fly || !Blocked(transform.position)))
            { Fly = !Fly; vertical = 0; Grounded = false; }
            float dt = Mathf.Min(Time.deltaTime, .05f);
            Vector3 wish = new Vector3((k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0), 0,
                (k.wKey.isPressed ? 1 : 0) - (k.sKey.isPressed ? 1 : 0));
            wish = transform.TransformDirection(Vector3.ClampMagnitude(wish, 1)) * Speed * (k.leftShiftKey.isPressed ? 1.8f : 1);
            if (Fly)
            {
                wish.y = ((k.spaceKey.isPressed ? 1 : 0) - (k.leftCtrlKey.isPressed ? 1 : 0)) * Speed;
                transform.position += wish * dt; return;
            }
            if (Grounded && k.spaceKey.wasPressedThisFrame) vertical = 5.2f;
            vertical = Mathf.Max(vertical - 17 * dt, -15); wish.y = vertical;
            // Substeps below half a cell prevent tunneling across thin voxel walls.
            Vector3 displacement = wish * dt;
            int steps = Mathf.Max(1, Mathf.CeilToInt(displacement.magnitude / (World.VoxelSize * .45f)));
            Vector3 step = displacement / steps;
            Grounded = false;
            for (int i = 0; i < steps; i++)
            {
                MoveAxis(new Vector3(step.x, 0, 0), true);
                MoveAxis(new Vector3(0, 0, step.z), true);
                if (!MoveAxis(new Vector3(0, step.y, 0), false)) { if (step.y < 0) Grounded = true; vertical = 0; }
            }
            if (transform.position.y < -4) Spawn(new Vector3(World.SizeX * World.VoxelSize / 2, World.SizeY * World.VoxelSize + 1, World.SizeZ * World.VoxelSize / 2));
        }
        bool MoveAxis(Vector3 delta, bool canStep)
        {
            if (delta.sqrMagnitude < 1e-12f) return true;
            Vector3 p = transform.position + delta;
            if (!Blocked(p)) { transform.position = p; return true; }
            // Small voxel stairs are traversable while on the floor.
            if (canStep && vertical <= 0)
            {
                float rise = World.VoxelSize * 1.1f;
                if (!Blocked(p + Vector3.up * rise) && Blocked(transform.position - Vector3.up * .03f))
                { transform.position = p + Vector3.up * rise; return true; }
            }
            return false;
        }
        public bool Blocked(Vector3 feet)
        {
            float s = World.VoxelSize;
            int x0 = Mathf.FloorToInt((feet.x - Radius) / s), x1 = Mathf.FloorToInt((feet.x + Radius) / s);
            int z0 = Mathf.FloorToInt((feet.z - Radius) / s), z1 = Mathf.FloorToInt((feet.z + Radius) / s);
            int y0 = Mathf.FloorToInt((feet.y + .015f) / s), y1 = Mathf.FloorToInt((feet.y + Height) / s);
            if (x0 < 0 || z0 < 0 || x1 >= World.SizeX || z1 >= World.SizeZ || y0 < 0) return true;
            for (int z = z0; z <= z1; z++) for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                if (MaterialCatalog.BlocksActorBody(World.Get(x, y, z), CollideWithSoftFoliage)) return true;
            return false;
        }
    }
}
