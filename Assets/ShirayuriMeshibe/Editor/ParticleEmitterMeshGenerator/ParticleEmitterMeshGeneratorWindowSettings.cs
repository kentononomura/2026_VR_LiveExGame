using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShirayuriMeshibe.ParticleEmitterMeshGenerator
{
    [FilePath("UserSettings/ParticleEmitterMeshGeneratorWindow/Settings.dat", FilePathAttribute.Location.ProjectFolder)]
    public sealed class ParticleEmitterMeshGeneratorWindowSettings : EditorWindowSettings<ParticleEmitterMeshGeneratorWindowSettings>
    {
        [field: SerializeField] public Property<string> OutputFolderPath { get; private set; }
        [field: SerializeField] public Property<MeshBuildDirectionType> MeshBuildDirection { get; private set; }
        [field: SerializeField] public Property<SortType> Sort { get; private set; }
        [field: SerializeField] public Property<float> AudienceDistanceX { get; private set; }
        [field: SerializeField] public Property<float> AudienceDistanceZ { get; private set; }
        [field: SerializeField] public Property<float> AudienceRandomDistance { get; private set; }
        [field: SerializeField] public Property<int> AudienceCountX { get; private set; }
        [field: SerializeField] public Property<int> AudienceCountZ { get; private set; }
        [field: SerializeField] public Property<int> RandomSeed { get; private set; }
        [field: SerializeField] public Property<AreaSettings[]> AreaSettings { get; private set; }

        ParticleEmitterMeshGeneratorWindowSettings()
        {
            OutputFolderPath = new Property<string>(this, string.Empty);
            MeshBuildDirection = new Property<MeshBuildDirectionType>(this, MeshBuildDirectionType.RightBackward);
            Sort = new Property<SortType>(this, SortType.Random);
            AudienceDistanceX = new Property<float>(this, 0.4f);
            AudienceDistanceZ = new Property<float>(this, 0.4f);
            AudienceRandomDistance = new Property<float>(this, 0.13f);
            AudienceCountX = new Property<int>(this, 40);
            AudienceCountZ = new Property<int>(this, 30);
            RandomSeed = new Property<int>(this, 0);
            AreaSettings = new Property<AreaSettings[]>(this, Array.Empty<AreaSettings>());
        }

        public void WriteToJson(string fullpath)
        {
            var json = JsonUtility.ToJson(this, true);
            File.WriteAllText(fullpath, json);
        }
    }
}
