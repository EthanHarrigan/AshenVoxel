using System;
using AshenVoxel.WorldScale;
using AshenVoxel.WorldScaleRuntime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AshenVoxel.Exploration
{
    /// <summary>Basic first-person input and view for the exploration prototype.
    /// Owns no editing tools. Global double movement is independent of render rebasing.</summary>
    public sealed class ExplorationController:MonoBehaviour
    {
        [Range(.01f,.5f)] public float MouseSensitivity=.10f;
        [Range(1,60)] public float GroundedEyeSmoothing=20;
        public bool AllowNoClipToggle=true;
        public bool CapturePointerOnEnable=true;
        public bool InputEnabled {get;private set;}=true;
        public bool IsConfigured=>Motor!=null&&vista!=null;
        public ExplorationMotor Motor {get;private set;}
        public WorldPoint Feet=>Motor==null?default:Motor.Feet;
        public WorldPoint Eye=>Motor==null?default:new WorldPoint(Motor.Feet.X,eyeY,Motor.Feet.Z);
        public string Status {get;private set;}="Preparing player…";
        public float Yaw {get;private set;}
        public float Pitch {get;private set;}
        public double DeferredMovementSeconds=>pendingSeconds;
        public double SkippedLongFrameInputSeconds {get;private set;}
        WorldExplorer vista;
        double eyeY,pendingSeconds;
        bool eyeInitialized,ownsPointer;

        public void Configure(WorldExplorer world,IExplorationCollision collision,WorldPoint spawnFeet,ExplorationMovementSettings settings=null,
            float initialYaw=0,float initialPitch=10,bool capturePointer=true)
        {
            vista=world??throw new ArgumentNullException(nameof(world));
            Motor=new ExplorationMotor(collision,spawnFeet,settings);
            Yaw=initialYaw;Pitch=Mathf.Clamp(initialPitch,-86,86);pendingSeconds=0;eyeInitialized=false;
            CapturePointerOnEnable=capturePointer;
            if(InputEnabled&&capturePointer)CapturePointer();
            ApplyView(0);Status="Ready";
        }
        public void ApplyMovementSettings(ExplorationMovementSettings settings)
        {if(settings==null)throw new ArgumentNullException(nameof(settings));if(Motor!=null)Motor.Settings=settings;}
        public void SetInputEnabled(bool value)
        {
            InputEnabled=value;pendingSeconds=0;
            if(!value)ReleasePointer();else if(IsConfigured&&CapturePointerOnEnable)CapturePointer();
        }
        public void ResetAt(WorldPoint feet,bool flying=false)
        {
            if(Motor==null)throw new InvalidOperationException("Configure the player before respawning.");
            Motor.ResetAt(feet,flying);pendingSeconds=0;eyeInitialized=false;ApplyView(0);
        }
        public bool TrySpawn(WorldPoint near,double searchRadius=12)
        {
            if(Motor==null)return false;
            if(!Motor.TryFindClearSpawn(near,searchRadius)){Status="Loading a clear starting area…";return false;}
            pendingSeconds=0;eyeInitialized=false;ApplyView(0);Status="Walking";return true;
        }
        public void CapturePointer(){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;ownsPointer=true;}
        public void ReleasePointer(){if(ownsPointer||Cursor.lockState==CursorLockMode.Locked){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}ownsPointer=false;}
        void Update()
        {
            if(!IsConfigured||!InputEnabled)return;
            Keyboard keyboard=Keyboard.current;Mouse mouse=Mouse.current;
            if(keyboard!=null&&keyboard.escapeKey.wasPressedThisFrame)
            {
                if(Cursor.lockState==CursorLockMode.Locked)ReleasePointer();else CapturePointer();
                pendingSeconds=0;return;
            }
            if(Cursor.lockState!=CursorLockMode.Locked)
            {
                if(mouse!=null&&mouse.leftButton.wasPressedThisFrame)CapturePointer();
                pendingSeconds=0;Status="Click to capture pointer · Escape releases it";return;
            }
            if(mouse!=null)
            {
                Vector2 delta=mouse.delta.ReadValue();
                Yaw+=delta.x*MouseSensitivity;Pitch=Mathf.Clamp(Pitch-delta.y*MouseSensitivity,-86,86);
                // Keep the scalar angles bounded during long flights without changing orientation.
                if(Yaw>36000||Yaw< -36000)Yaw%=360;
            }
            if(keyboard!=null&&keyboard.fKey.wasPressedThisFrame)
            {
                if(!Motor.SetFlying(!Motor.Flying))Status="Move into clear space before returning to walking.";
            }
            if(keyboard!=null&&AllowNoClipToggle&&Motor.Flying&&keyboard.nKey.wasPressedThisFrame)Motor.NoClip=!Motor.NoClip;
            double localX=keyboard==null?0:(keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0);
            double localZ=keyboard==null?0:(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0);
            double radians=Yaw*Math.PI/180,co=Math.Cos(radians),si=Math.Sin(radians);
            double x=localX*co+localZ*si,z=localZ*co-localX*si;
            double vertical=keyboard==null?0:((keyboard.spaceKey.isPressed||keyboard.eKey.isPressed)?1:0)-((keyboard.leftCtrlKey.isPressed||keyboard.qKey.isPressed)?1:0);
            var input=new ExplorationMoveInput(x,z,vertical,keyboard!=null&&keyboard.leftShiftKey.isPressed,keyboard!=null&&keyboard.spaceKey.wasPressedThisFrame);
            Advance(input,Math.Max(0,Time.deltaTime));
        }
        /// <summary>Public deterministic-input bridge for bootstrap/testing. A long
        /// input sample is capped at250ms; solver-budget remainder is carried.
        /// Streaming holds clear it rather than replaying old input after loading.</summary>
        public void Advance(ExplorationMoveInput input,double frameSeconds)
        {
            if(!IsConfigured||!InputEnabled)return;
            if(double.IsNaN(frameSeconds)||double.IsInfinity(frameSeconds)||frameSeconds<0)throw new ArgumentOutOfRangeException(nameof(frameSeconds));
            double accepted=Math.Min(.25,frameSeconds);SkippedLongFrameInputSeconds+=Math.Max(0,frameSeconds-accepted);
            pendingSeconds+=accepted;Motor.Tick(input,pendingSeconds);pendingSeconds=Motor.DeferredSeconds;
            ApplyView(accepted);
            Status=Motor.WaitingForWorld?"Loading nearby terrain and solid objects…":Motor.Flying?(Motor.NoClip?"Flying · noclip":"Flying · collision enabled"):
                Motor.Grounded?"Walking":"Airborne";
        }
        void ApplyView(double dt)
        {
            if(!IsConfigured)return;
            double target=Motor.Feet.Y+Motor.Settings.EyeHeight;
            if(!eyeInitialized||!Motor.Grounded||Motor.Flying||Math.Abs(target-eyeY)>Motor.Settings.StepHeight+.12)
            {eyeY=target;eyeInitialized=true;}
            else eyeY+=(target-eyeY)*(1-Math.Exp(-Math.Max(1,GroundedEyeSmoothing)*dt));
            // Vertical camera smoothing must remain inside the already tested body.
            eyeY=Math.Max(Motor.Feet.Y+.1,Math.Min(Motor.Feet.Y+Motor.Settings.Height-.025,eyeY));
            vista.SetObserverPose(Eye,Quaternion.Euler(Pitch,Yaw,0));
        }
        void OnApplicationFocus(bool focused){if(!focused){if(ownsPointer)ReleasePointer();pendingSeconds=0;}}
        void OnDisable(){if(ownsPointer)ReleasePointer();pendingSeconds=0;}
    }
}
