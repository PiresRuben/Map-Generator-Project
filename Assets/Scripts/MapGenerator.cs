using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    // ====================================================================
    // 1. PROPRIÉTÉS DE LA MAP ET DU RELIEF (Bruit Fractal)
    // ====================================================================

    [Header("Dimensions Générales")]
    public int width = 200;
    public int depth = 200;
    public Vector2 offset;
    public int seed = 0;
    public Transform tileContainer;

    [Header("Réglages du Relief (Bruit Fractal)")]
    public float scale = 80f;
    public int octaves = 5;
    [Range(0, 1)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    public float heightMultiplier = 60f;
    public AnimationCurve heightCurve;

    [Header("Réglages Spécifiques")]
    [Range(0f, 1f)] public float seaLevel = 0.3f;
    public float bottomLevel = -10f; // Profondeur du socle (Mode Pilier)
    [Range(1f, 5f)] public float mountainExaggeration = 1.5f;

    [Header("Biomes")]
    public BiomePreset[] biomes;

    // ====================================================================
    // 2. PROPRIÉTÉS DE LA VILLE (Quadtree Irrégulier)
    // ====================================================================

    [Header("City")]
    [Range(0, 250)] public int citySize = 130;
    public GameObject roadPrefab;
    public GameObject[] buildingPrefabs;
    [Range(0f, 4f)] public float buildingDensity = 0.015f;

    [Header("L-System / Quadtree")]
    [Range(2, 8)] public int lSystemDepth = 8;
    public float mainRoadChance = 0.9f;
    public float subdivisionChance = 1.1f;
    public int minBlockSize = 2;

    // ====================================================================
    // 3. VARIABLES INTERNES
    // ====================================================================

    private Dictionary<string, Material> biomeMaterials = new();
    private HashSet<Vector2Int> roadPositions = new();
    private Vector2[] octaveOffsets;

    // ====================================================================
    // 4. UNITY LIFECYCLE & INITIALISATION
    // ====================================================================

    void Start()
    {
        if (seed == 0)
            seed = Random.Range(0, 999999);

        // Assure que la courbe de hauteur existe
        if (heightCurve == null || heightCurve.length < 2)
        {
            heightCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.5f, 0.2f), new Keyframe(1, 1));
        }

        GenerateMap();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            seed = Random.Range(0, 999999);
            GenerateMap();
        }
    }

    public void ClearAll()
    {
        if (tileContainer != null) Destroy(tileContainer.gameObject);
        GameObject container = new GameObject("MapContainer");
        tileContainer = container.transform;

        biomeMaterials.Clear();
        roadPositions.Clear();
    }

    // ====================================================================
    // 5. GÉNÉRATION GLOBALE 
    // ====================================================================

    public void GenerateMap()
    {
        Random.InitState(seed);
        ClearAll();
        GenerateBiomeMaterials();

        System.Random prng = new System.Random(seed);
        octaveOffsets = new Vector2[octaves];
        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000) + offset.x;
            float offsetY = prng.Next(-100000, 100000) + offset.y;
            octaveOffsets[i] = new Vector2(offsetX, offsetY);
        }

        GenerateRoadNetwork();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                // Calcul de la hauteur de terrain pour le sol
                float noiseHeight = GetFractalNoise(x, z, octaveOffsets);
                float finalHeight = heightCurve.Evaluate(noiseHeight);
                float moistureValue = Mathf.PerlinNoise((x + seed + 500) / scale, (z + seed + 500) / scale);

                BiomePreset biome = GetBiome(finalHeight, moistureValue);
                bool isWater = false;

                // Vérification du niveau de la mer (S'applique au terrain ET à la ville pour le raccord)
                if (finalHeight <= seaLevel)
                {
                    isWater = true;
                    finalHeight = seaLevel;
                    biome = GetBiomeByName("Sea");
                }

                if (IsInCity(x, z))
                {
                    // La ville se raccorde à la hauteur finale calculée
                    GenerateCityTile(x, z, finalHeight);
                }
                else
                {
                    // Exagération des montagnes uniquement hors de la ville
                    if (biome.name == "Mountain")
                    {
                        finalHeight *= mountainExaggeration;
                    }

                    CreateTile(x, z, finalHeight, biome, isWater);
                }
            }
        }
    }

    // ====================================================================
    // 6. CITY GENERATION (Quadtree + Irregularity Logic)
    // ====================================================================

    void GenerateRoadNetwork()
    {
        int size = Mathf.Min(citySize, Mathf.Min(width, depth) - 2);
        if (size <= 0) return;

        int startX = (width / 2) - size / 2;
        int startZ = (depth / 2) - size / 2;
        int centerH = depth / 2;
        int centerW = width / 2;

        // CROIX CENTRALE (Base de la connexion)
        for (int x = startX; x < startX + size; x++)
        {
            roadPositions.Add(new Vector2Int(x, centerH));
            roadPositions.Add(new Vector2Int(centerW, x));
        }

        // Quadtree / L-System (Logique modifiée pour l'irrégularité)
        Subdivide(startX, startZ, size, size, lSystemDepth);
    }

    void Subdivide(int x, int z, int w, int h, int depth)
    {
        if (depth <= 0 || w < minBlockSize * 2 || h < minBlockSize * 2 || Random.value > subdivisionChance)
        {
            return;
        }

        int splitX = (int)Mathf.Lerp(minBlockSize, w - minBlockSize, Random.Range(0.3f, 0.7f));
        int splitZ = (int)Mathf.Lerp(minBlockSize, h - minBlockSize, Random.Range(0.3f, 0.7f));

        int roadX = x + splitX;
        int roadZ = z + splitZ;

        bool drawH = Random.value < mainRoadChance;
        bool drawV = Random.value < mainRoadChance;

        if (!drawH && !drawV)
        {
            if (Random.value < 0.5f) drawH = true;
            else drawV = true;
        }

        // --- TRACÉ DES ROUTES ---
        if (drawH)
        {
            for (int i = x; i < x + w; i++) roadPositions.Add(new Vector2Int(i, roadZ));
        }
        if (drawV)
        {
            for (int j = z; j < z + h; j++) roadPositions.Add(new Vector2Int(roadX, j));
        }

        // --- RÉCURSION ---
        Subdivide(x, z, splitX, splitZ, depth - 1);
        Subdivide(x + splitX, z, w - splitX, splitZ, depth - 1);
        Subdivide(x, z + splitZ, splitX, h - splitZ, depth - 1);
        Subdivide(x + splitX, z + splitZ, w - splitX, h - splitZ, depth - 1);
    }

    bool IsInCity(int x, int z)
    {
        int size = Mathf.Min(citySize, Mathf.Min(width, depth) - 2);
        if (size <= 0) return false;

        int sx = width / 2 - size / 2;
        int sz = depth / 2 - size / 2;

        return x >= sx && x < sx + size && z >= sz && z < sz + size;
    }

    void GenerateCityTile(int x, int z, float baseTerrainHeight)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        // Raccord de la hauteur
        float cityY = baseTerrainHeight * heightMultiplier;

        tile.transform.position = new Vector3(x, cityY, z);

        bool isRoad = roadPositions.Contains(new Vector2Int(x, z));

        var renderer = tile.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.material.color = isRoad ? Color.black : Color.gray;

        if (isRoad && roadPrefab != null)
        {
            // Positionnement des routes légèrement au-dessus du sol
            Instantiate(roadPrefab, new Vector3(x, cityY + 0.05f, z), Quaternion.identity, tile.transform);
        }
        else if (IsNearRoad(x, z) && Random.value < buildingDensity && buildingPrefabs.Length > 0)
        {
            // Logique de rotation des bâtiments
            Quaternion buildingRotation = Quaternion.identity;
            if (roadPositions.Contains(new Vector2Int(x, z + 1))) buildingRotation = Quaternion.Euler(0, 0, 0);
            else if (roadPositions.Contains(new Vector2Int(x, z - 1))) buildingRotation = Quaternion.Euler(0, 180, 0);
            else if (roadPositions.Contains(new Vector2Int(x + 1, z))) buildingRotation = Quaternion.Euler(0, 90, 0);
            else if (roadPositions.Contains(new Vector2Int(x - 1, z))) buildingRotation = Quaternion.Euler(0, 270, 0);

            GameObject b = buildingPrefabs[Random.Range(0, buildingPrefabs.Length)];

            GameObject go = Instantiate(
                b,
                new Vector3(x, cityY + 0.5f, z), // Positionnement ajusté à la hauteur du sol
                buildingRotation,
                tile.transform
            );

            go.transform.localScale *= Random.Range(0.95f, 1.1f);
            OptimizeProp(go);
        }
    }

    bool IsNearRoad(int x, int z)
    {
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (roadPositions.Contains(new Vector2Int(x + i, z + j)))
                    return true;
        return false;
    }

    // ====================================================================
    // 7. TERRAIN & BIOME TILES (Logique du Pilier)
    // ====================================================================

    void CreateTile(int x, int z, float heightValue, BiomePreset biome, bool isWater)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        // --- Mode Pilier (pour boucher les trous) ---
        float worldY = Mathf.Floor(heightValue * heightMultiplier);
        float floorBottom = bottomLevel;
        float pillarHeight = worldY - floorBottom;

        tile.transform.localScale = new Vector3(1, pillarHeight, 1);
        tile.transform.position = new Vector3(x, floorBottom + (pillarHeight / 2), z);

        Renderer rend = tile.GetComponent<Renderer>();
        if (biomeMaterials.ContainsKey(biome.name))
        {
            rend.sharedMaterial = biomeMaterials[biome.name];
        }
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;

        if (!isWater)
        {
            SpawnProps(tile.transform, biome, pillarHeight);
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

    // ====================================================================
    // 8. HELPERS & OPTIMIZATION
    // ====================================================================

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
            if (!biomeMaterials.ContainsKey(biome.name))
                biomeMaterials.Add(biome.name, mat);
        }
    }

    // Seuil ajusté à 0.7f pour équilibrer la terre et l'eau
    BiomePreset GetBiome(float height, float moisture)
    {
        if (height > 0.7f) return GetBiomeByName("Mountain");
        if (moisture < 0.4f) return GetBiomeByName("Desert");
        return GetBiomeByName("GrassField");
    }

    BiomePreset GetBiomeByName(string name)
    {
        foreach (var b in biomes) if (b.name == name) return b;
        return biomes.Length > 0 ? biomes[0] : new BiomePreset();
    }

    void SpawnProps(Transform parentTile, BiomePreset biome, float pillarScaleY)
    {
        if (biome.props == null || biome.props.Length == 0) return;

        float actualDensity = biome.propDensity;
        if (biome.name == "Mountain") actualDensity *= 0.5f;

        if (Random.value < actualDensity)
        {
            GameObject propPrefab = biome.props[Random.Range(0, biome.props.Length)];
            if (propPrefab != null)
            {
                Vector3 spawnPos = new Vector3(parentTile.position.x, parentTile.position.y + (pillarScaleY / 2), parentTile.position.z);

                GameObject prop = Instantiate(propPrefab, spawnPos + Vector3.up * 0.5f, Quaternion.identity);
                prop.transform.parent = parentTile;

                Vector3 originalScale = Vector3.one * Random.Range(0.8f, 1.2f);
                prop.transform.localScale = new Vector3(originalScale.x, originalScale.y / pillarScaleY, originalScale.z);

                prop.transform.Rotate(0, Random.Range(0, 360), 0);
                OptimizeProp(prop);
            }
        }
    }

    void OptimizeProp(GameObject obj)
    {
        foreach (Renderer r in obj.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            foreach (Material m in r.sharedMaterials)
            {
                if (m != null)
                {
                    m.enableInstancing = true;
                    m.SetFloat("_SpecularHighlights", 0f);
                    m.SetFloat("_GlossyReflections", 0f);
                }
            }
        }
    }
}

// ====================================================================
// STRUCTURES
// ====================================================================

[System.Serializable]
public struct BiomePreset
{
    public string name;
    public Color color;
    [Range(0, 1)] public float propDensity;
    public GameObject[] props;
}