using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

namespace UnitySkills
{
    /// <summary>
    /// Model import settings skills - get/set model importer properties (FBX, OBJ, etc).
    /// </summary>
    public static class ModelSkills
    {
        [UnitySkill("model_get_settings", "Get model import settings for a 3D model asset (FBX, OBJ, etc)")]
        public static object ModelGetSettings(string assetPath)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return new { error = $"Not a model file or asset not found: {assetPath}" };

            return new
            {
                success = true,
                path = assetPath,
                // Scene
                globalScale = importer.globalScale,
                useFileScale = importer.useFileScale,
                importBlendShapes = importer.importBlendShapes,
                importVisibility = importer.importVisibility,
                importCameras = importer.importCameras,
                importLights = importer.importLights,
                // Meshes
                meshCompression = importer.meshCompression.ToString(),
                isReadable = importer.isReadable,
                optimizeMeshPolygons = importer.optimizeMeshPolygons,
                optimizeMeshVertices = importer.optimizeMeshVertices,
                generateSecondaryUV = importer.generateSecondaryUV,
                // Geometry
                keepQuads = importer.keepQuads,
                weldVertices = importer.weldVertices,
                // Normals & Tangents
                importNormals = importer.importNormals.ToString(),
                importTangents = importer.importTangents.ToString(),
                // Animation
                animationType = importer.animationType.ToString(),
                importAnimation = importer.importAnimation,
                // Materials
                materialImportMode = importer.materialImportMode.ToString()
            };
        }

        [UnitySkill("model_set_settings", "Set model import settings. meshCompression: Off/Low/Medium/High. animationType: None/Legacy/Generic/Humanoid. materialImportMode: None/ImportViaMaterialDescription/ImportStandard")]
        public static object ModelSetSettings(
            string assetPath,
            float? globalScale = null,
            bool? useFileScale = null,
            bool? importBlendShapes = null,
            bool? importVisibility = null,
            bool? importCameras = null,
            bool? importLights = null,
            string meshCompression = null,
            bool? isReadable = null,
            bool? optimizeMeshPolygons = null,
            bool? optimizeMeshVertices = null,
            bool? generateSecondaryUV = null,
            bool? keepQuads = null,
            bool? weldVertices = null,
            string importNormals = null,
            string importTangents = null,
            string animationType = null,
            bool? importAnimation = null,
            string materialImportMode = null)
        {
            if (Validate.Required(assetPath, "assetPath") is object err) return err;

            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
                return new { error = $"Not a model file or asset not found: {assetPath}" };

            // 修改前记录资产状态
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset != null) WorkflowManager.SnapshotObject(asset);

            var changes = new List<string>();

            // Scene settings
            if (globalScale.HasValue)
            {
                importer.globalScale = globalScale.Value;
                changes.Add($"globalScale={globalScale.Value}");
            }

            if (useFileScale.HasValue)
            {
                importer.useFileScale = useFileScale.Value;
                changes.Add($"useFileScale={useFileScale.Value}");
            }

            if (importBlendShapes.HasValue)
            {
                importer.importBlendShapes = importBlendShapes.Value;
                changes.Add($"importBlendShapes={importBlendShapes.Value}");
            }

            if (importVisibility.HasValue)
            {
                importer.importVisibility = importVisibility.Value;
                changes.Add($"importVisibility={importVisibility.Value}");
            }

            if (importCameras.HasValue)
            {
                importer.importCameras = importCameras.Value;
                changes.Add($"importCameras={importCameras.Value}");
            }

            if (importLights.HasValue)
            {
                importer.importLights = importLights.Value;
                changes.Add($"importLights={importLights.Value}");
            }

            // Mesh settings
            if (!string.IsNullOrEmpty(meshCompression))
            {
                if (System.Enum.TryParse<ModelImporterMeshCompression>(meshCompression, true, out var mc))
                {
                    importer.meshCompression = mc;
                    changes.Add($"meshCompression={mc}");
                }
                else
                {
                    return new { error = $"Invalid meshCompression: {meshCompression}. Valid: Off, Low, Medium, High" };
                }
            }

            if (isReadable.HasValue)
            {
                importer.isReadable = isReadable.Value;
                changes.Add($"isReadable={isReadable.Value}");
            }

            if (optimizeMeshPolygons.HasValue)
            {
                importer.optimizeMeshPolygons = optimizeMeshPolygons.Value;
                changes.Add($"optimizeMeshPolygons={optimizeMeshPolygons.Value}");
            }

            if (optimizeMeshVertices.HasValue)
            {
                importer.optimizeMeshVertices = optimizeMeshVertices.Value;
                changes.Add($"optimizeMeshVertices={optimizeMeshVertices.Value}");
            }

            if (generateSecondaryUV.HasValue)
            {
                importer.generateSecondaryUV = generateSecondaryUV.Value;
                changes.Add($"generateSecondaryUV={generateSecondaryUV.Value}");
            }

            // Geometry
            if (keepQuads.HasValue)
            {
                importer.keepQuads = keepQuads.Value;
                changes.Add($"keepQuads={keepQuads.Value}");
            }

            if (weldVertices.HasValue)
            {
                importer.weldVertices = weldVertices.Value;
                changes.Add($"weldVertices={weldVertices.Value}");
            }

            // Normals & Tangents
            if (!string.IsNullOrEmpty(importNormals))
            {
                if (System.Enum.TryParse<ModelImporterNormals>(importNormals, true, out var normals))
                {
                    importer.importNormals = normals;
                    changes.Add($"importNormals={normals}");
                }
            }

            if (!string.IsNullOrEmpty(importTangents))
            {
                if (System.Enum.TryParse<ModelImporterTangents>(importTangents, true, out var tangents))
                {
                    importer.importTangents = tangents;
                    changes.Add($"importTangents={tangents}");
                }
            }

            // Animation
            if (!string.IsNullOrEmpty(animationType))
            {
                if (System.Enum.TryParse<ModelImporterAnimationType>(animationType, true, out var at))
                {
                    importer.animationType = at;
                    changes.Add($"animationType={at}");
                }
                else
                {
                    return new { error = $"Invalid animationType: {animationType}. Valid: None, Legacy, Generic, Humanoid" };
                }
            }

            if (importAnimation.HasValue)
            {
                importer.importAnimation = importAnimation.Value;
                changes.Add($"importAnimation={importAnimation.Value}");
            }

            // Materials
            if (!string.IsNullOrEmpty(materialImportMode))
            {
                if (System.Enum.TryParse<ModelImporterMaterialImportMode>(materialImportMode, true, out var mim))
                {
                    importer.materialImportMode = mim;
                    changes.Add($"materialImportMode={mim}");
                }
            }

            // Apply changes
            importer.SaveAndReimport();

            return new
            {
                success = true,
                path = assetPath,
                changesApplied = changes.Count,
                changes
            };
        }

        [UnitySkill("model_set_settings_batch", "Set model import settings for multiple 3D models. items: JSON array of {assetPath, meshCompression, animationType, ...}")]
        public static object ModelSetSettingsBatch(string items)
        {
            return BatchExecutor.Execute<BatchModelItem>(items, item =>
            {
                var importer = AssetImporter.GetAtPath(item.assetPath) as ModelImporter;
                if (importer == null)
                    throw new System.Exception("Not a model file");

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.assetPath);
                if (asset != null) WorkflowManager.SnapshotObject(asset);

                if (item.globalScale.HasValue) importer.globalScale = item.globalScale.Value;
                if (item.importBlendShapes.HasValue) importer.importBlendShapes = item.importBlendShapes.Value;
                if (item.importCameras.HasValue) importer.importCameras = item.importCameras.Value;
                if (item.importLights.HasValue) importer.importLights = item.importLights.Value;
                if (item.isReadable.HasValue) importer.isReadable = item.isReadable.Value;
                if (item.generateSecondaryUV.HasValue) importer.generateSecondaryUV = item.generateSecondaryUV.Value;
                if (item.importAnimation.HasValue) importer.importAnimation = item.importAnimation.Value;

                if (!string.IsNullOrEmpty(item.meshCompression) &&
                    System.Enum.TryParse<ModelImporterMeshCompression>(item.meshCompression, true, out var mc))
                    importer.meshCompression = mc;

                if (!string.IsNullOrEmpty(item.animationType) &&
                    System.Enum.TryParse<ModelImporterAnimationType>(item.animationType, true, out var at))
                    importer.animationType = at;

                if (!string.IsNullOrEmpty(item.materialImportMode) &&
                    System.Enum.TryParse<ModelImporterMaterialImportMode>(item.materialImportMode, true, out var mim))
                    importer.materialImportMode = mim;

                importer.SaveAndReimport();
                return new { path = item.assetPath, success = true };
            }, item => item.assetPath,
            setup: () => AssetDatabase.StartAssetEditing(),
            teardown: () => { AssetDatabase.StopAssetEditing(); AssetDatabase.Refresh(); });
        }

        private class BatchModelItem
        {
            public string assetPath { get; set; }
            public float? globalScale { get; set; }
            public bool? importBlendShapes { get; set; }
            public bool? importCameras { get; set; }
            public bool? importLights { get; set; }
            public string meshCompression { get; set; }
            public bool? isReadable { get; set; }
            public bool? generateSecondaryUV { get; set; }
            public string animationType { get; set; }
            public bool? importAnimation { get; set; }
            public string materialImportMode { get; set; }
        }
    }
}
