using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

public class MapGenerator : MonoBehaviour
{
    [Header("Map")]
    public int width = 150;
    public int depth = 150;
    public float scale = 18f;
    public float heightMultiplier = 6f;
    public int seed = 0;
    public Vector2 offset;

    [Header("Biomes")]
    public BiomePreset[] biomes;

    [Header("City")]
    [Range(0, 250)] public int citySize = 130;
    public GameObject roadPrefab;
    public GameObject[] buildingPrefabs;
    [Range(0f, 4f)] public float buildingDensity = 0.015f; // très faible

    [Header("L-System / Quadtree")]
    // Modifié: Augmenter la profondeur max pour plus de détails
    [Range(2, 8)] public int lSystemDepth = 8;
    // Modifié: Forte chance de créer des routes dans les blocs finaux
    public float mainRoadChance = 0.9f;
    // Modifié: Très forte chance de subdiviser un bloc ( > 1 pour garantir la division aux premiers niveaux)
    public float subdivisionChance = 1.1f;
    // Modifié: Réduire la taille minimale pour des blocs plus fins
    public int minBlockSize = 2;

    public Transform tileContainer;

    private Dictionary<string, Material> biomeMaterials = new();
    private HashSet<Vector2Int> roadPositions = new();

    void Start()
    {
        if (seed == 0)
            seed = Random.Range(0, 999999);

        GenerateImmediate();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            seed = Random.Range(0, 999999);
            GenerateImmediate();
        }
    }

    // ========================= GENERATE ==========================

    void GenerateImmediate()
    {
        GenerateMap();
    }

    public void ClearAll()
    {
        if (tileContainer != null)
            Destroy(tileContainer.gameObject);

        tileContainer = new GameObject("MapContainer").transform;

        biomeMaterials.Clear();
        roadPositions.Clear();
    }

    public void GenerateMap()
    {
        Random.InitState(seed);

        ClearAll();

        GenerateBiomeMaterials();
        GenerateRoadNetwork();

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                if (IsInCity(x, z))
                {
                    GenerateCityTile(x, z);
                }
                else
                {
                    float height = CalculateNoise(x, z, scale, seed);
                    float moisture = CalculateNoise(x, z, scale, seed + 500);
                    CreateTile(x, z, height, GetBiome(height, moisture));
                }
            }
        }
    }

    // ===================== CITY (L-SYSTEM + QUADTREE) =======================

    void GenerateRoadNetwork()
    {
        int size = Mathf.Min(citySize, Mathf.Min(width, depth) - 2);

        if (size <= 0)
            return;

        int startX = (width / 2) - size / 2;
        int startZ = (depth / 2) - size / 2;

        // CROIX CENTRALE
        for (int x = startX; x < startX + size; x++)
        {
            roadPositions.Add(new Vector2Int(x, depth / 2));
            roadPositions.Add(new Vector2Int(width / 2, x));
        }

        // Quadtree / L-System
        Subdivide(startX, startZ, size, size, lSystemDepth);
    }

    void Subdivide(int x, int z, int w, int h, int depth)
    {
        // Si le bloc est assez petit OU qu'on a atteint la limite de profondeur
        // OU que le hasard décide de ne pas subdiviser ce bloc précis :
        if (depth <= 0 || w < minBlockSize || h < minBlockSize || Random.value > subdivisionChance)
        {
            // On dessine une route qui traverse ce bloc (créant une rue finale)
            if (Random.value < mainRoadChance)
            {
                // On choisit aléatoirement si la route est verticale ou horizontale
                // pour varier l'alignement des quartiers
                bool vertical = Random.value > 0.5f;

                if (vertical)
                {
                    int rx = x + w / 2;
                    for (int i = z; i < z + h; i++)
                        roadPositions.Add(new Vector2Int(rx, i));
                }
                else
                {
                    int rz = z + h / 2;
                    for (int i = x; i < x + w; i++)
                        roadPositions.Add(new Vector2Int(i, rz));
                }
            }
            return;
        }

        // Si on est ici, c'est qu'on a décidé de subdiviser le bloc en 4 sous-blocs
        int hw = w / 2;
        int hh = h / 2;

        Subdivide(x, z, hw, hh, depth - 1);
        Subdivide(x + hw, z, hw, hh, depth - 1);
        Subdivide(x, z + hh, hw, hh, depth - 1);
        Subdivide(x + hw, z + hh, hw, hh, depth - 1);
    }


    bool IsInCity(int x, int z)
    {
        int size = Mathf.Min(citySize, Mathf.Min(width, depth) - 2);
        if (size <= 0) return false;

        int sx = width / 2 - size / 2;
        int sz = depth / 2 - size / 2;

        return x >= sx && x < sx + size && z >= sz && z < sz + size;
    }

    void GenerateCityTile(int x, int z)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;
        tile.transform.position = new Vector3(x, 0, z);

        bool isRoad = roadPositions.Contains(new Vector2Int(x, z));

        var renderer = tile.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        renderer.material.color = isRoad ? Color.black : Color.gray;

        if (isRoad && roadPrefab != null)
        {
            Instantiate(roadPrefab, new Vector3(x, 0.05f, z), Quaternion.identity, tile.transform);
        }
        else if (IsNearRoad(x, z) && Random.value < buildingDensity && buildingPrefabs.Length > 0)
        {
            // --- LOGIQUE DE ROTATION ---
            Quaternion buildingRotation = Quaternion.identity;

            // On regarde où est la route pour orienter la façade
            // Note : Cela suppose que la "face avant" de ton prefab regarde vers Z+ (Forward)
            if (roadPositions.Contains(new Vector2Int(x, z + 1)))
                buildingRotation = Quaternion.Euler(0, 0, 0);       // Route au Nord -> Regarde le Nord
            else if (roadPositions.Contains(new Vector2Int(x, z - 1)))
                buildingRotation = Quaternion.Euler(0, 180, 0);     // Route au Sud -> Regarde le Sud
            else if (roadPositions.Contains(new Vector2Int(x + 1, z)))
                buildingRotation = Quaternion.Euler(0, 90, 0);      // Route à l'Est -> Regarde l'Est
            else if (roadPositions.Contains(new Vector2Int(x - 1, z)))
                buildingRotation = Quaternion.Euler(0, 270, 0);     // Route à l'Ouest -> Regarde l'Ouest

            // Si c'est un coin (plusieurs routes), le 'else if' prendra la première trouvée, ce qui est suffisant.

            GameObject b = buildingPrefabs[Random.Range(0, buildingPrefabs.Length)];

            GameObject go = Instantiate(
                b,
                new Vector3(x, 0.5f, z),
                buildingRotation, // On applique la rotation calculée
                tile.transform
            );

            // J'ai retiré le scale aléatoire excessif pour garder une uniformité urbaine, 
            // mais tu peux le remettre si tu veux varier la hauteur.
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

    // ===================== BIOMES =======================

    void CreateTile(int x, int z, float height, BiomePreset biome)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.transform.parent = tileContainer;

        float yPos = biome.name == "Sea" ? 1 : Mathf.Floor(height * heightMultiplier);
        tile.transform.position = new Vector3(x, yPos, z);

        var r = tile.GetComponent<Renderer>();
        if (biomeMaterials.ContainsKey(biome.name))
            r.sharedMaterial = biomeMaterials[biome.name];

        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;

        SpawnProps(tile.transform, biome);
    }

    // ===================== HELPERS =======================

    void GenerateBiomeMaterials()
    {
        biomeMaterials.Clear();

        foreach (var biome in biomes)
        {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = biome.color;
            mat.enableInstancing = true;
            mat.SetFloat("_GlossyReflections", 0);
            mat.SetFloat("_SpecularHighlights", 0);

            if (!biomeMaterials.ContainsKey(biome.name))
                biomeMaterials.Add(biome.name, mat);
        }
    }

    float CalculateNoise(int x, int z, float scale, int seed)
    {
        return Mathf.PerlinNoise(
          (float)x / width * scale + offset.x + seed,
          (float)z / depth * scale + offset.y + seed
        );
    }

    void SpawnProps(Transform tile, BiomePreset biome)
    {
        if (biome.props == null || biome.props.Length == 0)
            return;

        if (Random.value < biome.propDensity)
        {
            GameObject prefab = biome.props[Random.Range(0, biome.props.Length)];

            if (prefab != null)
            {
                GameObject obj = Instantiate(
                  prefab,
                  tile.position + Vector3.up * 0.5f,
                  Quaternion.Euler(0, Random.Range(0, 360), 0),
                  tile
                );

                OptimizeProp(obj);
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
                    m.SetFloat("_GlossyReflections", 0);
                    m.SetFloat("_SpecularHighlights", 0);
                }
            }
        }
    }

    // ===================== BIOME LOGIC =======================

    BiomePreset GetBiome(float h, float m)
    {
        if (h < 0.3f) return GetBiomeByName("Sea");
        if (h > 0.75f) return GetBiomeByName("Mountain");
        if (m < 0.4f) return GetBiomeByName("Desert");

        return GetBiomeByName("GrassField");
    }

    BiomePreset GetBiomeByName(string name)
    {
        foreach (var b in biomes)
            if (b.name == name)
                return b;

        return biomes.Length > 0 ? biomes[0] : new BiomePreset();
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