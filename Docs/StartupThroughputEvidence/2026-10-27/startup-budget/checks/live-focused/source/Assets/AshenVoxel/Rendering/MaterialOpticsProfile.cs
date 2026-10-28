using System;
using AshenVoxel.Core;
using UnityEngine;

namespace AshenVoxel.Rendering
{
    [CreateAssetMenu(menuName="Ashen Voxel/Material optics")]
    public sealed class MaterialOpticsProfile : ScriptableObject
    {
        [Serializable] public sealed class Entry
        {
            public MaterialId Material;
            [Range(0,1),Tooltip("0 is invisible, 1 is opaque. Coverage is attached to surfaces. Leaves add up to 6% fade only within 0.8m of the eye.")]
            public float Opacity=1;
            [Range(0,1),Tooltip("Soft light through the material, independent of visibility and collision.")]
            public float Transmission;
        }
        public Entry[] Materials;
        static readonly Vector4[] values=new Vector4[32];
        public static MaterialOpticsProfile Active {get;private set;}
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Initialize(){Active=Resources.Load<MaterialOpticsProfile>("MaterialOptics");Apply(Active);}
        void OnValidate(){if(Application.isPlaying&&(Active==this||Active==null))Apply(this);}
        public static void Apply(MaterialOpticsProfile profile=null)
        {
            for(int i=0;i<32;i++)
            {
                if(i<MaterialCatalog.Count){var id=(MaterialId)i;var d=MaterialCatalog.Get(id);values[i]=new Vector4(DefaultOpacity(id,d.Opacity),d.Transmission,1,0);}
                else values[i]=new Vector4(1,0,1,0);
            }
            if(profile!=null&&profile.Materials!=null)foreach(var e in profile.Materials)
                if(e!=null&&MaterialCatalog.IsValid(e.Material)&&!float.IsNaN(e.Opacity)&&!float.IsNaN(e.Transmission))
                    values[(int)e.Material]=new Vector4(Mathf.Clamp01(e.Opacity),Mathf.Clamp01(e.Transmission),1,0);
            Shader.SetGlobalVectorArray("_VoxelMaterialOptics",values);
        }
        // Rendering defaults are independent of material authority/save data.
        // Match the shipped profile even when it is absent or only overrides
        // one material (as in authoring fixtures).
        static float DefaultOpacity(MaterialId material,float catalogOpacity)
        {
            switch(material)
            {
                case MaterialId.Leaves:return 1;
                case MaterialId.Ice:return .98f;
                case MaterialId.Petal:return .99f;
                case MaterialId.Web:return .65f;
                default:return catalogOpacity;
            }
        }
        public static void SetAmbient(Color sky,Color equator,Color ground,float strength=1)
        {
            if(QualitySettings.activeColorSpace==ColorSpace.Linear){sky=sky.linear;equator=equator.linear;ground=ground.linear;}
            strength=float.IsNaN(strength)?1:Mathf.Clamp(strength,0,2);sky*=strength;equator*=strength;ground*=strength;
            sky.a=1;equator.a=1;ground.a=1;
            Shader.SetGlobalVector("_VoxelAmbientSky",sky);Shader.SetGlobalVector("_VoxelAmbientEquator",equator);Shader.SetGlobalVector("_VoxelAmbientGround",ground);
        }
        public static void ClearAmbient(){Shader.SetGlobalVector("_VoxelAmbientSky",Vector4.zero);}
    }
}
