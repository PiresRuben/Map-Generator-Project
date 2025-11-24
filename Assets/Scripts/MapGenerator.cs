using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    [Header("Paramètres de la Carte")]
    public int width = 50;
    public int depth = 50;
    public float scale = 20f;
    public float heightMultiplier = 5f;
    public int seed = 0;
    public Vector2 offset;

    [Header("Configuration des Biomes")]
    public BiomePreset[] biomes;

    [Header("Paramètres Généraux")]
    public Transform tileContainer;

    private Dictionary<string, Material> biomeMaterials = new Dictionary<string, Material>();

    private void Start()
    {
        if (seed == 0) seed = Random.Range(0, 100000);

        GenerateBiomeMaterials();

        GenerateMap();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            seed = Random.Range(0, 100000);
            GenerateMap();
        }
    }

    void GenerateBiomeMaterials()
    {
        biomeMaterials.Clear();
        foreach (var biome in biomes)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = biome.color;

            mat.enableInstancing = true;

            mat.SetFloat("_SpecularHighlights", 0f);

            mat.SetFloat("_GlossyReflections", 0f);

            biomeMaterials.Add(biome.name, mat);
        }
    }

    public void GenerateMap()
    {
        if (tileContainer != null) Destroy(tileContainer.gameObject);

        GameObject container = new GameObject("MapContainer");
        tileContainer = container.transform;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                float heightValue = CalculateNoise(x, z, scale, seed);
                float moistureValue = CalculateNoise(x, z, scale, seed + 500);
                BiomePreset biome = GetBiome(heightValue, moistureValue);

                CreateTile(x, z, heightValue, biome);
            }
        }
    }

    void CreateTile(int x, int z, float heightValue, BiomePreset biome)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        float yPos = Mathf.Floor(heightValue * heightMultiplier);
        if (biome.name == "Sea") yPos = Mathf.Floor(0.3f * heightMultiplier);
        tile.transform.position = new Vector3(x, yPos, z);

        Renderer rend = tile.GetComponent<Renderer>();

        if (biomeMaterials.ContainsKey(biome.name))
        {
            rend.sharedMaterial = biomeMaterials[biome.name];
        }

        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        SpawnProps(tile.transform, biome);
    }

    void SpawnProps(Transform parentTile, BiomePreset biome)
    {
        if (biome.props.Length == 0) return;

        if (Random.value < biome.propDensity)
        {
            GameObject propPrefab = biome.props[Random.Range(0, biome.props.Length)];
            if (propPrefab != null)
            {
                GameObject prop = Instantiate(propPrefab, parentTile.position + Vector3.up * 0.5f, Quaternion.identity);
                prop.transform.parent = parentTile;
                prop.transform.Rotate(0, Random.Range(0, 360), 0);

                OptimizeProp(prop);
            }
        }
    }

    void OptimizeProp(GameObject prop)
    {
        Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            foreach (Material mat in r.sharedMaterials)
            {
                if (mat != null)
                {
                    mat.enableInstancing = true;

                    mat.SetFloat("_SpecularHighlights", 0f);
                    mat.SetFloat("_GlossyReflections", 0f);
                }
            }
        }
    }

    float CalculateNoise(int x, int z, float scale, int seed)
    {
        float xCoord = (float)x / width * scale + offset.x + seed;
        float zCoord = (float)z / depth * scale + offset.y + seed;
        return Mathf.PerlinNoise(xCoord, zCoord);
    }

    BiomePreset GetBiome(float height, float moisture)
    {
        if (height < 0.3f) return GetBiomeByName("Sea");
        if (height > 0.7f) return GetBiomeByName("Mountain");
        if (moisture < 0.4f) return GetBiomeByName("Desert");
        return GetBiomeByName("GrassField");
    }

    BiomePreset GetBiomeByName(string name)
    {
        foreach (var b in biomes) if (b.name == name) return b;
        return biomes[0];
    }
}

[System.Serializable]
public struct BiomePreset
{
    public string name;
    public Color color;
    [Range(0, 1)] public float propDensity;
    public GameObject[] props;
}