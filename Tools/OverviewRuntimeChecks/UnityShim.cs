using System.Text.Json;
namespace UnityEngine
{
    public class MonoBehaviour{}
    public static class Time{public static double unscaledTimeAsDouble;}
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options=new JsonSerializerOptions{IncludeFields=true};
        public static T FromJson<T>(string json)=>JsonSerializer.Deserialize<T>(json,Options);
        public static string ToJson(object value)=>JsonSerializer.Serialize(value,value.GetType(),Options);
    }
}
namespace AshenVoxel.WorldScaleRuntime
{
    // Only the controller's rendering boundary is substituted. Static transport
    // and actual overview data/identity/CRC/SHA implementations are production C#.
    public sealed class WorldExplorer
    {
        public bool VoxelMemoryPressureBlocked,HasActiveCoverage;
        public void RefreshExternalRegion(double minX,double minZ,double maxX,double maxZ){}
    }
}
