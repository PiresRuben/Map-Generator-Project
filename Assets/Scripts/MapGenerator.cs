using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    [Header("Dimensions Map")]
    public int width = 200;
    public int depth = 200;
    public Vector2 offset;

    [Header("Réglages du Relief (Nature)")]
    public float scale = 80f;
    public int octaves = 5;
    [Range(0, 1)] public float persistance = 0.5f;
    public float lacunarity = 2f;
    public float heightMultiplier = 60f;
    public AnimationCurve heightCurve;

    [Header("Réglages Montagne & Eau")]
    [Range(1f, 5f)] public float mountainExaggeration = 1.5f;
    [Range(0f, 1f)] public float seaLevel = 0.3f;
    public float bottomLevel = -10f;

    [Header("Réglages Ville (City)")]
    [Range(0, 250)] public int citySize = 100;
    [Range(0f, 1f)] public float cityAltitude = 0.35f;
    public GameObject roadPrefab;
    public GameObject[] buildingPrefabs;
    [Range(0f, 1f)] public float buildingDensity = 0.5f;

    [Header("Ville : L-System / Routes")]
    [Range(2, 8)] public int lSystemDepth = 5;
    public float mainRoadChance = 0.9f;
    public float subdivisionChance = 1.1f;
    public int minBlockSize = 3;

    [Header("Seed & Système")]
    public int seed = 0;
    public Transform tileContainer;

    [Header("Biomes")]
    public BiomePreset[] biomes;

    private Dictionary<string, Material> biomeMaterials = new Dictionary<string, Material>();
    private HashSet<Vector2Int> roadPositions = new HashSet<Vector2Int>();

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


    public void GenerateMap()
    {
        if (tileContainer != null) Destroy(tileContainer.gameObject);
        GameObject container = new GameObject("MapContainer");
        tileContainer = container.transform;

        roadPositions.Clear();
        Random.InitState(seed);

        GenerateRoadNetwork();

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
                if (IsInCity(x, z))
                {
                    CreateCityTile(x, z);
                }
                else
                {
                    float noiseHeight = GetFractalNoise(x, z, octaveOffsets);
                    float finalHeight = heightCurve.Evaluate(noiseHeight);
                    float moistureValue = Mathf.PerlinNoise((x + seed + 500) / scale, (z + seed + 500) / scale);

                    BiomePreset biome = GetBiome(finalHeight, moistureValue);
                    bool isWater = false;

                    if (finalHeight <= seaLevel)
                    {
                        isWater = true;
                        finalHeight = seaLevel;
                        biome = GetBiomeByName("Sea");
                    }
                    else if (biome.name == "Mountain")
                    {
                        finalHeight *= mountainExaggeration;
                    }

                    CreateNatureTile(x, z, finalHeight, biome, isWater);
                }
            }
        }
    }

    void GenerateRoadNetwork()
    {
        int size = Mathf.Min(citySize, Mathf.Min(width, depth) - 2);
        if (size <= 0) return;

        int startX = (width / 2) - size / 2;
        int startZ = (depth / 2) - size / 2;

        for (int x = startX; x < startX + size; x++)
        {
            roadPositions.Add(new Vector2Int(x, depth / 2));
            roadPositions.Add(new Vector2Int(width / 2, x));
        }

        Subdivide(startX, startZ, size, size, lSystemDepth);
    }

    void Subdivide(int x, int z, int w, int h, int depth)
    {
        if (depth <= 0 || w < minBlockSize * 2 || h < minBlockSize * 2 || Random.value > subdivisionChance)
            return;

        int splitX = (int)Mathf.Lerp(minBlockSize, w - minBlockSize, Random.Range(0.3f, 0.7f));
        int splitZ = (int)Mathf.Lerp(minBlockSize, h - minBlockSize, Random.Range(0.3f, 0.7f));

        int roadX = x + splitX;
        int roadZ = z + splitZ;

        bool drawH = Random.value < mainRoadChance;
        bool drawV = Random.value < mainRoadChance;

        if (!drawH && !drawV) { if (Random.value < 0.5f) drawH = true; else drawV = true; }

        if (drawH)
            for (int i = x; i < x + w; i++) roadPositions.Add(new Vector2Int(i, roadZ));

        if (drawV)
            for (int j = z; j < z + h; j++) roadPositions.Add(new Vector2Int(roadX, j));

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

    void CreateNatureTile(int x, int z, float heightValue, BiomePreset biome, bool isWater)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;
        tile.name = $"Nature_{x}_{z}";

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
        OptimizeRenderer(rend);

        if (!isWater)
        {
            SpawnNatureProps(tile.transform, biome, pillarHeight);
        }
    }

    void CreateCityTile(int x, int z)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;
        tile.name = $"City_{x}_{z}";

        float safeCityHeight = Mathf.Max(cityAltitude, seaLevel + 0.05f);
        float worldY = Mathf.Floor(safeCityHeight * heightMultiplier);
        float floorBottom = bottomLevel;
        float pillarHeight = worldY - floorBottom;

        tile.transform.localScale = new Vector3(1, pillarHeight, 1);
        tile.transform.position = new Vector3(x, floorBottom + (pillarHeight / 2), z);

        bool isRoad = roadPositions.Contains(new Vector2Int(x, z));

        Renderer rend = tile.GetComponent<Renderer>();
        Material cityMat = new Material(Shader.Find("Standard"));
        cityMat.color = isRoad ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.8f, 0.8f, 0.8f);
        cityMat.enableInstancing = true;
        rend.material = cityMat;
        OptimizeRenderer(rend);

        Vector3 topPosition = new Vector3(x, floorBottom + pillarHeight, z);

        if (isRoad && roadPrefab != null)
        {
            GameObject road = Instantiate(roadPrefab, topPosition, Quaternion.identity, tile.transform);

            float targetThickness = 0.02f;
            road.transform.localScale = new Vector3(1, targetThickness / pillarHeight, 1);

            OptimizeProp(road);
        }
        else if (IsNearRoad(x, z) && Random.value < buildingDensity && buildingPrefabs.Length > 0)
        {
            SpawnBuilding(x, z, topPosition, tile.transform, pillarHeight);
        }
    }

    void SpawnBuilding(int x, int z, Vector3 position, Transform parent, float pillarScaleY)
    {
        Quaternion rot = Quaternion.identity;
        if (roadPositions.Contains(new Vector2Int(x, z + 1))) rot = Quaternion.Euler(0, 0, 0);
        else if (roadPositions.Contains(new Vector2Int(x, z - 1))) rot = Quaternion.Euler(0, 180, 0);
        else if (roadPositions.Contains(new Vector2Int(x + 1, z))) rot = Quaternion.Euler(0, 90, 0);
        else if (roadPositions.Contains(new Vector2Int(x - 1, z))) rot = Quaternion.Euler(0, 270, 0);

        GameObject prefab = buildingPrefabs[Random.Range(0, buildingPrefabs.Length)];
        GameObject building = Instantiate(prefab, position, rot, parent);

        Vector3 originalScale = Vector3.one * Random.Range(0.9f, 1.1f);
        building.transform.localScale = new Vector3(originalScale.x, originalScale.y / pillarScaleY, originalScale.z);

        OptimizeProp(building);
    }

    bool IsNearRoad(int x, int z)
    {
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                if (roadPositions.Contains(new Vector2Int(x + i, z + j))) return true;
        return false;
    }

    void SpawnNatureProps(Transform parentTile, BiomePreset biome, float pillarScaleY)
    {
        if (biome.props.Length == 0) return;

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

    void OptimizeRenderer(Renderer r)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void OptimizeProp(GameObject prop)
    {
        Renderer[] renderers = prop.GetComponentsInChildren<Renderer>();
        foreach (Renderer r in renderers)
        {
            OptimizeRenderer(r);
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

    BiomePreset GetBiome(float height, float moisture)
    {
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