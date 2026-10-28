using System;
using AshenVoxel.WorldScale;
namespace UnityEngine
{
 public sealed class RangeAttribute:Attribute{public RangeAttribute(float a,float b){}}
 public class MonoBehaviour{}
 public enum CursorLockMode{None,Locked}
 public static class Cursor{public static CursorLockMode lockState;public static bool visible;}
 public static class Time{public static float deltaTime=1f/60;}
 public static class Mathf{public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v));}
 public struct Vector2{public float x,y;public Vector2(float x,float y){this.x=x;this.y=y;}}
 public struct Quaternion{public float x,y,z;public static Quaternion Euler(float x,float y,float z)=>new Quaternion{x=x,y=y,z=z};}
}
namespace UnityEngine.InputSystem
{
 public class ButtonControl{public bool isPressed,wasPressedThisFrame;}
 public class Vector2Control{public UnityEngine.Vector2 Value;public UnityEngine.Vector2 ReadValue()=>Value;}
 public sealed class Keyboard
 {
  public static Keyboard current;
  public ButtonControl wKey=new ButtonControl(),aKey=new ButtonControl(),sKey=new ButtonControl(),dKey=new ButtonControl(),qKey=new ButtonControl(),eKey=new ButtonControl(),fKey=new ButtonControl(),nKey=new ButtonControl(),spaceKey=new ButtonControl(),leftCtrlKey=new ButtonControl(),leftShiftKey=new ButtonControl(),escapeKey=new ButtonControl();
 }
 public sealed class Mouse{public static Mouse current;public Vector2Control delta=new Vector2Control();public ButtonControl leftButton=new ButtonControl();}
}
namespace AshenVoxel.WorldScaleRuntime
{
 public sealed class WorldExplorer
 {
  public WorldPoint LastEye;public UnityEngine.Quaternion LastRotation;public int ObserverWrites;
  public void SetObserverPose(WorldPoint eye,UnityEngine.Quaternion rotation){LastEye=eye;LastRotation=rotation;ObserverWrites++;}
 }
}
