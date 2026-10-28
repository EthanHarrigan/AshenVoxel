using System;
namespace UnityEngine
{
 public enum LightType { Directional } public enum LightShadows { Soft }
 public sealed class Light:Component { public LightType type; public LightShadows shadows;public float intensity;public Color color; }
 public static class RenderSettings {public static Rendering.AmbientMode ambientMode;public static Color ambientSkyColor,ambientEquatorColor,ambientGroundColor;public static bool fog;public static Material skybox;}
 public static class GUILayout
 {
  public static string PressedButton;
  public static bool Button(string text) { if(text!=PressedButton)return false;PressedButton=null;return true; }
 }
}
namespace UnityEngine.Rendering {public enum AmbientMode {Trilight}}
namespace UnityEditor
{
 public sealed class CustomEditor:Attribute {public CustomEditor(Type type){}}
 public sealed class MenuItem:Attribute {public MenuItem(string text){}}
 public class Editor {public UnityEngine.Object target;public virtual void OnInspectorGUI(){}protected void DrawDefaultInspector(){}}
 public static class EditorApplication {public static bool isPlaying;}
 public enum MessageType {Info}
 public static class EditorGUILayout {public static void HelpBox(string text,MessageType type){}}
 public static class EditorGUI {public sealed class DisabledScope:IDisposable {public DisabledScope(bool disabled){}public void Dispose(){}}}
 public static class Selection {public static UnityEngine.GameObject activeGameObject;}
}
namespace UnityEditor.SceneManagement
{
 public enum NewSceneSetup {EmptyScene}public enum NewSceneMode {Single}
 public static class EditorSceneManager
 {
  public static bool SaveCurrentModifiedScenesIfUserWantsTo()=>true;
  public static void OpenScene(string path){}
  public static object NewScene(NewSceneSetup setup,NewSceneMode mode)=>new object();
  public static bool SaveScene(object scene,string path)=>true;
 }
}
