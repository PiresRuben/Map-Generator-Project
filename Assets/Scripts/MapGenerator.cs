using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    [Header("Dimensions")]
    public int width = 200; // Réduit un peu par défaut pour les tests
    public int depth = 200;
    public Vector2 offset;

    [Header("Réglages du Relief Global")]
    public float scale = 80f;
    public int octaves = 5;
    [Range(0, 1)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    // AUGMENTE CE CHIFFRE POUR DU VRAI RELIEF (ex: 60)
    public float heightMultiplier = 60f;
    public AnimationCurve heightCurve;

    [Header("Réglages Spécifiques Montagne")]
    // NOUVEAU : Force les montagnes à être beaucoup plus hautes et pentues
    [Range(1f, 5f)]
    public float mountainExaggeration = 1.5f;

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

        // Initialisation d'une courbe en "S" par défaut si elle est vide pour un meilleur résultat immédiat
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

                // 3. Humidité pour les biomes
                float moistureValue = Mathf.PerlinNoise((x + seed + 500) / scale, (z + seed + 500) / scale);

                // 4. Choix du biome
                BiomePreset biome = GetBiome(finalHeight, moistureValue);

                // --- LE FIX POUR LA MONTAGNE DE CUBES ---
                // Si le biome choisi est une montagne, on applique un bonus de hauteur
                // Cela va créer des falaises de cubes entre la plaine et la montagne.
                if (biome.name == "Mountain")
                {
                    finalHeight *= mountainExaggeration;
                }

                CreateTile(x, z, finalHeight, biome);
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

    void CreateTile(int x, int z, float heightValue, BiomePreset biome)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        // Calcul de la position Y finale (l'aspect "blocs")
        float yPos = Mathf.Floor(heightValue * heightMultiplier);

        if (biome.name == "Sea") yPos = Mathf.Floor(0.2f * heightMultiplier);

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
        // Réduction de la densité sur les pentes abruptes (optionnel, pour le réalisme)
        float actualDensity = biome.propDensity;
        if (biome.name == "Mountain") actualDensity *= 0.5f;

        if (Random.value < actualDensity)
        {
            GameObject propPrefab = biome.props[Random.Range(0, biome.props.Length)];
            if (propPrefab != null)
            {
                // Ajustement de la position Y pour que l'objet soit posé sur le cube, pas dedans
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

    BiomePreset GetBiome(float height, float moisture)
    {
        if (height < 0.3f) return GetBiomeByName("Sea");
        // J'ai baissé le seuil de la montagne pour qu'elle apparaisse plus souvent
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