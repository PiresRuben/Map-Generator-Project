using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    [Header("Dimensions")]
    public int width = 200;
    public int depth = 200;
    public Vector2 offset;

    [Header("Réglages du Relief Global")]
    public float scale = 80f;
    public int octaves = 5;
    [Range(0, 1)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    public float heightMultiplier = 60f;
    public AnimationCurve heightCurve;

    [Header("Réglages Spécifiques Montagne")]
    [Range(1f, 5f)]
    public float mountainExaggeration = 1.5f;

    [Header("Réglages Eau & Sol (Nouveau)")]
    [Range(0f, 1f)] public float seaLevel = 0.3f; // Niveau fixe de l'eau
    public float bottomLevel = -10f; // Profondeur du socle pour boucher les trous

    [Header("Seed")]
    public int seed = 0;

    [Header("Biomes")]
    public BiomePreset[] biomes;

    [Header("Système")]
    public Transform tileContainer;

    private Dictionary<string, Material> biomeMaterials = new Dictionary<string, Material>();

    private void Start()
    {
        if (seed == 0) seed = Random.Range(0, 100000);

        if (heightCurve == null || heightCurve.length < 2)
        {
            heightCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 0.2f), new Keyframe(1, 1));
        }

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

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;
            float offsetY = prng.Next(-100000, 100000) + offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                // 1. Hauteur de base (Fractal Noise)
                float noiseHeight = GetFractalNoise(x, z, octaveOffsets);

                // 2. Application de la courbe
                float finalHeight = heightCurve.Evaluate(noiseHeight);

                // 3. Humidité
                float moistureValue = Mathf.PerlinNoise((x + seed + 500) / scale, (z + seed + 500) / scale);

                // 4. Choix du biome initial
                BiomePreset biome = GetBiome(finalHeight, moistureValue);

                // --- GESTION EAU & MONTAGNE ---
                bool isWater = false;

                // Si la hauteur est sous le niveau de la mer, on force l'eau plate
                if (finalHeight <= seaLevel)
                {
                    isWater = true;
                    finalHeight = seaLevel; // On aplatit l'eau
                    biome = GetBiomeByName("Sea"); // On force le biome Mer
                }
                else
                {
                    // Si ce n'est pas de l'eau et que c'est une montagne, on exagère la hauteur
                    if (biome.name == "Mountain")
                    {
                        finalHeight *= mountainExaggeration;
                    }
                }

                CreateTile(x, z, finalHeight, biome, isWater);
            }
        }
    }

    float GetFractalNoise(int x, int z, Vector2[] octaveOffsets)
    {
        float amplitude = 1;
        float frequency = 1;
        float noiseHeight = 0;
        float maxPossibleHeight = 0;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = x / scale * frequency + octaveOffsets[i].x;
            float sampleZ = z / scale * frequency + octaveOffsets[i].y;
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ);
            noiseHeight += perlinValue * amplitude;
            maxPossibleHeight += amplitude;
            amplitude *= persistance;
            frequency *= lacunarity;
        }
        return noiseHeight / maxPossibleHeight;
    }

    void CreateTile(int x, int z, float heightValue, BiomePreset biome, bool isWater)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        // --- CORRECTION TROUS (Mode Pilier) ---

        // 1. Calcul de la hauteur Y du sommet du bloc
        float worldY = Mathf.Floor(heightValue * heightMultiplier);

        // 2. Définition du bas du bloc (le socle)
        float floorBottom = bottomLevel;

        // 3. Calcul de la taille totale du pilier
        float pillarHeight = worldY - floorBottom;

        // 4. Étirement du cube pour qu'il aille du fond jusqu'à la surface
        tile.transform.localScale = new Vector3(1, pillarHeight, 1);

        // 5. Positionnement au centre du cube étiré
        tile.transform.position = new Vector3(x, floorBottom + (pillarHeight / 2), z);


        Renderer rend = tile.GetComponent<Renderer>();
        if (biomeMaterials.ContainsKey(biome.name))
        {
            rend.sharedMaterial = biomeMaterials[biome.name];
        }
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        // On ne fait spawn les props que si ce n'est pas de l'eau
        if (!isWater)
        {
            SpawnProps(tile.transform, biome, pillarHeight);
        }
    }

    void SpawnProps(Transform parentTile, BiomePreset biome, float pillarScaleY)
    {
        if (biome.props.Length == 0) return;

        float actualDensity = biome.propDensity;
        if (biome.name == "Mountain") actualDensity *= 0.5f;

        if (Random.value < actualDensity)
        {
            GameObject propPrefab = biome.props[Random.Range(0, biome.props.Length)];
            if (propPrefab != null)
            {
                // Positionnement sur le haut du pilier
                Vector3 spawnPos = new Vector3(parentTile.position.x, parentTile.position.y + (pillarScaleY / 2), parentTile.position.z);

                GameObject prop = Instantiate(propPrefab, spawnPos + Vector3.up * 0.5f, Quaternion.identity);
                prop.transform.parent = parentTile;

                // CORRECTION ÉCHELLE : On compense l'étirement du parent
                // Si le sol fait 20m de haut, on divise la taille de l'arbre par 20 sur l'axe Y
                Vector3 originalScale = Vector3.one * Random.Range(0.8f, 1.2f);
                prop.transform.localScale = new Vector3(originalScale.x, originalScale.y / pillarScaleY, originalScale.z);

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

    BiomePreset GetBiome(float height, float moisture)
    {
        // La mer est gérée avant l'appel de cette fonction via seaLevel
        if (height > 0.65f) return GetBiomeByName("Mountain");
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