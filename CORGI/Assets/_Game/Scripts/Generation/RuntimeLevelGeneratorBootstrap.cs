using UnityEngine;
using UnityEngine.SceneManagement;

namespace _Game.Scripts.Generation
{
    public static class RuntimeLevelGeneratorBootstrap
    {
        private const string DefaultCatalogResourcePath = "Generation/DefaultSpawnCatalog";
        private const string DefaultConfigResourcePath = "Generation/DefaultLevelGenerationConfig";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.Contains("Gen"))
            {
                return;
            }

            if (Object.FindFirstObjectByType<RuntimeLevelGenerator>() != null)
            {
                return;
            }

            var catalog = Resources.Load<SpawnCatalog>(DefaultCatalogResourcePath);
            var config = Resources.Load<LevelGenerationConfig>(DefaultConfigResourcePath);
            if (catalog == null || config == null)
            {
                Debug.LogWarning($"Procedural generation bootstrap could not find required assets at Resources/{DefaultCatalogResourcePath} and Resources/{DefaultConfigResourcePath}.");
                return;
            }

            var generatorObject = new GameObject("RuntimeLevelGenerator");
            var generator = generatorObject.AddComponent<RuntimeLevelGenerator>();
            generator.Initialize(catalog, config);
        }
    }
}

