// Test-only attribute/color substitutes. This does not compile or run the Unity engine.
using System;
namespace UnityEngine
{
    public class ScriptableObject : Object { }
    public sealed class CreateAssetMenuAttribute : Attribute { public string fileName, menuName; }
    public sealed class ColorUsageAttribute : Attribute { public ColorUsageAttribute(bool showAlpha) { } }
    public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string text) { } }
    public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }
    public sealed class RangeAttribute : Attribute { public RangeAttribute(float min,float max) { } }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1){this.r=r;this.g=g;this.b=b;this.a=a;}
        public static implicit operator Color(Color32 c)=>new Color(c.r/255f,c.g/255f,c.b/255f,c.a/255f);
        public static implicit operator Color32(Color c)=>new Color32((byte)Math.Round(Math.Clamp(c.r,0,1)*255),(byte)Math.Round(Math.Clamp(c.g,0,1)*255),(byte)Math.Round(Math.Clamp(c.b,0,1)*255),(byte)Math.Round(Math.Clamp(c.a,0,1)*255));
    }
    public struct Color32
    {
        public byte r,g,b,a;
        public Color32(byte r,byte g,byte b,byte a){this.r=r;this.g=g;this.b=b;this.a=a;}
    }
    public static class Mathf
    {
        public static float Clamp(float value,float min,float max)=>Math.Clamp(value,min,max);
        public static int Clamp(int value,int min,int max)=>Math.Clamp(value,min,max);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static int CeilToInt(float value)=>(int)Math.Ceiling(value);
        public static int FloorToInt(float value)=>(int)Math.Floor(value);
        public static float Exp(float value)=>(float)Math.Exp(value);
    }
}
