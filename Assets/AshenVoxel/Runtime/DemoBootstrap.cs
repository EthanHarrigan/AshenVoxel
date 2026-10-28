using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using AshenVoxel.Core;
using AshenVoxel.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Unity.Profiling;
using Debug = UnityEngine.Debug;

namespace AshenVoxel.Runtime
{
    public sealed class DemoBootstrap : MonoBehaviour
    {
        public int Seed = 714103;
        [Range(64, 384)] public int Width = 256;
        [Range(64, 192)] public int Height = 128;
        public const float VoxelSize = .1f;
        [Range(1, 32)] public int MeshesPerFrame = 4;
        [Range(100, 20000)] public int SimulationBudget = 1800;
        public VoxelWorld World { get; private set; }
        public Simulation Gravity { get; private set; }
        public ChunkWorldRenderer Terrain { get; private set; }
        public GridWalker Player { get; private set; }
        public bool SimulationPaused;
        float accumulator, brush = .45f, toolCooldown, smoothFrame, meshMs, simMs;
        MaterialId selected = MaterialId.Sand;
        string status = "Ready";
        GUIStyle text, title;
        bool ready;
        readonly List<GameObject> ownedObjects = new List<GameObject>();
        float hudRefresh;
        string hudTool = "", hudTimings = "", hudWork = "";
        int previousFrameRate, previousVSync;
        bool settingsCaptured;
        bool previousFog;
        FogMode previousFogMode;
        Color previousFogColor, previousSkyColor, previousEquatorColor, previousGroundColor;
        float previousFogDensity;
        AmbientMode previousAmbientMode;
        Material previousSkybox;
        readonly Stopwatch watch = new Stopwatch();
        static readonly ProfilerMarker GenerationMarker = new ProfilerMarker("Ashen.Generation");
        static readonly ProfilerMarker SimulationMarker = new ProfilerMarker("Ashen.Gravity");
        static readonly ProfilerMarker MeshMarker = new ProfilerMarker("Ashen.MeshRebuild");
        static readonly ProfilerMarker ToolMarker = new ProfilerMarker("Ashen.PlayerEdit");
        Material bossMaterial, eyeMaterial;
        CairnBoss boss;
        VolumeProfile atmosphere;
        public string SavePath => Path.Combine(Application.persistentDataPath, "ashen-demo.avxl");
        IEnumerator Start()
        {
            previousFrameRate = Application.targetFrameRate;
            previousVSync = QualitySettings.vSyncCount;
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            ConfigureLighting();
            status = "Generating seeded forest and strata...";
            yield return null;
            World = new VoxelWorld(Width, Height, Width, VoxelSize, Seed);
            using (GenerationMarker.Auto()) Generation.Generate(World);
            Gravity = new Simulation(World);
            Terrain = Child("Voxel surface cache").AddComponent<ChunkWorldRenderer>();
            Terrain.Initialize(World);
            // Startup is progressive; no unbounded RebuildAll on the gameplay frame.
            while (Terrain.PendingChunks > 0)
            {
                Terrain.RebuildBudget(24); status = "Building terrain surfaces: " + Terrain.PendingChunks;
                yield return null;
            }
            CreatePlayer(); CreateBoss();
            status = "Forest ready. Dig under the sand mound or pour sand into a crater.";
            ready = true;
            Cursor.lockState = CursorLockMode.Locked;
        }
        void CreatePlayer()
        {
            Player = Child("Grid player").AddComponent<GridWalker>();
            Player.World = World;
            var cam = new GameObject("Player Camera").AddComponent<Camera>();
            cam.tag = "MainCamera"; cam.transform.SetParent(Player.transform, false);
            cam.transform.localPosition = Vector3.up * 1.5f;
            cam.nearClipPlane = .035f; cam.farClipPlane = 180; cam.fieldOfView = 72;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.095f, .12f, .14f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            cam.gameObject.AddComponent<AudioListener>(); Player.View = cam;
            Respawn();
        }
        void Respawn()
        {
            int cx = World.SizeX / 2, cz = World.SizeZ / 2 - Mathf.RoundToInt(2.8f / World.VoxelSize);
            // Search body clearance, not a single height column; placed walls/canopies may occupy spawn.
            for (int ring = 0; ring <= 32; ring += 4)
            for (int dz = -ring; dz <= ring; dz += 4)
            for (int dx = -ring; dx <= ring; dx += 4)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 4 || z < 4 || x >= World.SizeX - 4 || z >= World.SizeZ - 4) continue;
                int y = World.SizeY - 1;
                while (y > 0 && World.Get(x,y,z) == MaterialId.Air) y--;
                Vector3 feet = new Vector3((x+.5f)*World.VoxelSize,(y+1)*World.VoxelSize+.04f,(z+.5f)*World.VoxelSize);
                if (!Player.Blocked(feet)) { Player.Spawn(feet); return; }
            }
            Player.Fly = true;
            Player.Spawn(new Vector3(cx*World.VoxelSize,World.SizeY*World.VoxelSize+2,cz*World.VoxelSize));
        }

        void Update()
        {
            if (!ready) return;
            smoothFrame = Mathf.Lerp(smoothFrame, Time.unscaledDeltaTime, .06f);
            float dt = Mathf.Min(Time.deltaTime, .1f);
            accumulator = Mathf.Min(accumulator + dt, 2f / 30);
            watch.Restart();
            while (accumulator >= 1f / 30)
            {
                if (!SimulationPaused)
                {
                    if (Gravity.PendingWakeCandidates > 0) Gravity.WakeBudget(SimulationBudget * 4);
                    using (SimulationMarker.Auto()) Gravity.Step(SimulationBudget);
                }
                accumulator -= 1f / 30;
            }
            watch.Stop(); simMs = (float)watch.Elapsed.TotalMilliseconds;
            using (ToolMarker.Auto()) HandleInput(dt);
            watch.Restart(); using (MeshMarker.Auto()) Terrain.RebuildBudget(MeshesPerFrame); watch.Stop();
            meshMs = (float)watch.Elapsed.TotalMilliseconds;
            hudRefresh -= Time.unscaledDeltaTime;
            if (hudRefresh <= 0)
            {
                hudRefresh = .25f;
                hudTool = "R respawn · F5 save · F9 load   |   " + selected + " / " + brush.ToString("F2") + " m";
                hudTimings = $"{(1/Mathf.Max(.001f,smoothFrame)):F0} fps · sim {simMs:F2} ms · mesh {meshMs:F2} ms";
                hudWork = $"active {Gravity.ActiveCount:N0} / scan {Gravity.PendingWakeCandidates:N0} · queued {Terrain.PendingChunks} · triangles {Terrain.TotalTriangles:N0}";
            }
        }
        void HandleInput(float dt)
        {
            var k = Keyboard.current; var mouse = Mouse.current;
            if (k != null)
            {
                if (k.pKey.wasPressedThisFrame) SimulationPaused = !SimulationPaused;
                if (k.rKey.wasPressedThisFrame) Respawn();
                if (k.f5Key.wasPressedThisFrame) Save();
                if (k.f9Key.wasPressedThisFrame) { Load(); return; }
                if (k.digit1Key.wasPressedThisFrame) selected = MaterialId.Sand;
                if (k.digit2Key.wasPressedThisFrame) selected = MaterialId.Dirt;
                if (k.digit3Key.wasPressedThisFrame) selected = MaterialId.Stone;
                if (k.digit4Key.wasPressedThisFrame) selected = MaterialId.Wood;
            }
            toolCooldown -= dt;
            if (mouse == null) return;
            brush = Mathf.Clamp(brush + mouse.scroll.ReadValue().y * .0008f, .15f, 1.2f);
            if (toolCooldown > 0 || Cursor.lockState != CursorLockMode.Locked) return;
            bool carve = mouse.leftButton.isPressed, place = mouse.rightButton.isPressed;
            if (!carve && !place) return;
            Vector3 o = Player.View.transform.position, d = Player.View.transform.forward;
            if (!VoxelRaycast.TryCast(World, o.x,o.y,o.z,d.x,d.y,d.z,18,out VoxelHit hit)) return;
            Vector3 p = o + d * hit.Distance;
            int count;
            if (carve) count = WorldEdits.CarveSphere(World,p.x,p.y,p.z,brush,10);
            else
            {
                // Keep a body-sized exclusion zone when building near the player.
                p -= d * brush * .75f;
                Vector3 body = Player.transform.position + Vector3.up * .8f;
                if (Vector3.Distance(p,body) < brush + 1.1f) { status = "Placement too close to player"; return; }
                count = WorldEdits.PlaceSphere(World,p.x,p.y,p.z,brush,selected);
            }
            status = (carve ? "Excavated " : "Placed ") + count + " cells";
            toolCooldown = .12f;
        }
        void Save()
        {
            try
            {
                // Atomic replacement: a failed save leaves the old snapshot intact.
                string temp = SavePath + ".tmp";
                using (var stream = File.Create(temp)) Persistence.Save(stream,World);
                if (File.Exists(SavePath)) File.Replace(temp,SavePath,null); else File.Move(temp,SavePath);
                status = "Snapshot saved";
            }
            catch (Exception e) { status = "Save failed: " + e.Message; Debug.LogException(e); }
        }
        void Load()
        {
            try
            {
                VoxelWorld loaded;
                using (var stream = File.OpenRead(SavePath)) loaded = Persistence.Load(stream);
                // Validate the demo contract before replacing live state.
                if (Mathf.Abs(loaded.VoxelSize - VoxelSize) > .000001f)
                    throw new InvalidDataException("This demo requires 10 cm cells.");
                if (loaded.SizeX < 64 || loaded.SizeX > 384 || loaded.SizeZ < 64 || loaded.SizeZ > 384 || loaded.SizeY < 64 || loaded.SizeY > 192)
                    throw new InvalidDataException("Snapshot dimensions exceed this finite demo's supported bounds.");
                // Prepare the replacement simulation before disposing the live one.
                var replacement = new Simulation(loaded);
                replacement.BeginWakeAll();
                if (boss != null) boss.CancelAttack();
                Gravity.Dispose(); World = loaded; Gravity = replacement;
                accumulator = 0; toolCooldown = .12f; hudRefresh = 0;
                Terrain.Initialize(World); Player.World = World; Respawn();
                // Rebuild resumes under the regular frame budget.
                if (boss != null) boss.ResetPosition();
                status = "Snapshot loaded; gravity rescanning incrementally";
            }
            catch (Exception e) { status = "Load failed: " + e.Message; Debug.LogException(e); }
        }
        void CreateBoss()
        {
            boss = Child("The Cairn — destruction test actor").AddComponent<CairnBoss>();
            boss.Demo = this;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            bossMaterial = new Material(lit) { color = new Color(.19f,.22f,.24f) };
            bossMaterial.SetFloat("_Smoothness", .13f);
            eyeMaterial = new Material(lit) { color = new Color(1,.27f,.04f) };
            eyeMaterial.EnableKeyword("_EMISSION"); eyeMaterial.SetColor("_EmissionColor",new Color(3,.45f,.05f));
            boss.Build(bossMaterial,eyeMaterial);
        }
        void ConfigureLighting()
        {
            settingsCaptured = true;
            previousFog = RenderSettings.fog; previousFogMode = RenderSettings.fogMode;
            previousFogColor = RenderSettings.fogColor; previousFogDensity = RenderSettings.fogDensity;
            previousAmbientMode = RenderSettings.ambientMode;
            previousSkyColor = RenderSettings.ambientSkyColor;
            previousEquatorColor = RenderSettings.ambientEquatorColor;
            previousGroundColor = RenderSettings.ambientGroundColor;
            previousSkybox = RenderSettings.skybox;
            RenderSettings.skybox = null;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(.12f,.16f,.18f); RenderSettings.fogDensity = .018f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.26f,.32f,.38f);
            RenderSettings.ambientEquatorColor = new Color(.16f,.18f,.19f);
            RenderSettings.ambientGroundColor = new Color(.07f,.065f,.06f);
            var sun = Child("Low evening sun").AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1,.77f,.53f); sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft; sun.shadowBias = .035f; sun.transform.rotation = Quaternion.Euler(27,-42,0);
            var volume = Child("Atmosphere").AddComponent<Volume>(); volume.isGlobal = true;
            atmosphere = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = atmosphere;
            var tone = atmosphere.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var color = atmosphere.Add<ColorAdjustments>(); color.postExposure.Override(.15f); color.contrast.Override(16); color.saturation.Override(-16);
            var bloom = atmosphere.Add<Bloom>(); bloom.intensity.Override(.16f); bloom.threshold.Override(1.2f);
            var vignette = atmosphere.Add<Vignette>(); vignette.intensity.Override(.2f);
        }
        void OnGUI()
        {
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(.86f,.88f,.84f) } };
                title = new GUIStyle(text) { fontSize = 24, fontStyle = FontStyle.Bold };
            }
            GUI.Box(new Rect(18,18,540,ready ? 226 : 100), GUIContent.none);
            GUI.Label(new Rect(34,28,510,34), "ASHEN / VOXEL FOUNDATION", title);
            GUI.Label(new Rect(34,66,510,24),status,text);
            if (!ready) return;
            GUI.Label(new Rect(34,94,510,24),"WASD move · mouse look · Space jump · Shift sprint · F fly",text);
            GUI.Label(new Rect(34,118,510,24),"LMB excavate · RMB pour/build · wheel brush · Esc release",text);
            GUI.Label(new Rect(34,142,510,24),"1 sand  2 dirt  3 stone  4 wood · B boss slam · P gravity",text);
            GUI.Label(new Rect(34,166,510,24),hudTool,text);
            GUI.Label(new Rect(34,190,510,24),hudTimings,text);
            GUI.Label(new Rect(34,214,510,24),hudWork,text);
            GUI.Label(new Rect(Screen.width/2-5, Screen.height/2-12,20,24),"+",text);
            GUI.Label(new Rect(24,Screen.height-36,Screen.width-48,24),"10 cm baseline • CPU simulation / greedy surfaces • production microvoxel rendering remains a profiling milestone",text);
        }
        GameObject Child(string label)
        {
            var go = new GameObject(label); go.transform.SetParent(transform,false); ownedObjects.Add(go); return go;
        }
        void OnDestroy()
        {
            ready = false;
            StopAllCoroutines();
            if (boss != null) boss.CancelAttack();
            Gravity?.Dispose();
            // Also handle removing only this component while its GameObject remains alive.
            foreach (var go in ownedObjects) if (go != null) Destroy(go);
            ownedObjects.Clear();
            if (settingsCaptured)
            {
                Application.targetFrameRate = previousFrameRate;
                QualitySettings.vSyncCount = previousVSync;
                RenderSettings.fog = previousFog; RenderSettings.fogMode = previousFogMode;
                RenderSettings.fogColor = previousFogColor; RenderSettings.fogDensity = previousFogDensity;
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientSkyColor = previousSkyColor;
                RenderSettings.ambientEquatorColor = previousEquatorColor;
                RenderSettings.ambientGroundColor = previousGroundColor;
                RenderSettings.skybox = previousSkybox;
            }
            if (atmosphere != null)
            {
                foreach (var component in atmosphere.components) if (component != null) Destroy(component);
                Destroy(atmosphere);
            }
            if (bossMaterial != null) Destroy(bossMaterial);
            if (eyeMaterial != null) Destroy(eyeMaterial);
            Cursor.lockState = CursorLockMode.None;
        }
    }
}
