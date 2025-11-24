using UnityEngine;
using System.Collections.Generic;

public class MapGenerator : MonoBehaviour
{
    [Header("Paramètres de la Carte")]
    public int width = 50;
    public int depth = 50;
    public float scale = 20f;       // Zoom sur le bruit (plus petit = plus de détails)
    public float heightMultiplier = 5f; // Intensité du relief
    public int seed = 0;
    public Vector2 offset;

    [Header("Configuration des Biomes")]
    public BiomePreset[] biomes;

    [Header("Paramètres Généraux")]
    public Transform tileContainer; // Pour ranger les cubes dans la hiérarchie

    private void Start()
    {
        // Générer une seed aléatoire si elle est à 0
        if (seed == 0) seed = Random.Range(0, 100000);
        GenerateMap();
    }

    private void Update()
    {
        // Appuyer sur Espace pour régénérer une nouvelle map
        if (Input.GetKeyDown(KeyCode.Space))
        {
            seed = Random.Range(0, 100000);
            GenerateMap();
        }
    }

    public void GenerateMap()
    {
        // Nettoyage de l'ancienne map
        if (tileContainer != null)
        {
            foreach (Transform child in tileContainer)
            {
                Destroy(child.gameObject);
            }
        }
        else
        {
            GameObject container = new GameObject("MapContainer");
            tileContainer = container.transform;
        }

        // Boucle de génération
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                // 1. Calculer la hauteur (Height Map)
                float heightValue = CalculateNoise(x, z, scale, seed);

                // 2. Calculer l'humidité/température (pour différencier Désert et Plaine)
                // On décale la seed pour ne pas avoir le même pattern que la hauteur
                float moistureValue = CalculateNoise(x, z, scale, seed + 500);

                // 3. Trouver le biome correspondant
                BiomePreset biome = GetBiome(heightValue, moistureValue);

                // 4. Créer le bloc de terrain
                CreateTile(x, z, heightValue, biome);
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
        // Logique de sélection du biome
        // Si la hauteur est très basse -> MER
        if (height < 0.3f) return GetBiomeByName("Sea");

        // Si la hauteur est très haute -> MONTAGNE
        if (height > 0.7f) return GetBiomeByName("Mountain");

        // Entre les deux (Terre ferme), on utilise l'humidité pour choisir entre Désert et Plaine
        if (moisture < 0.4f) return GetBiomeByName("Desert");

        return GetBiomeByName("GrassField");
    }

    BiomePreset GetBiomeByName(string name)
    {
        foreach (var b in biomes)
        {
            if (b.name == name) return b;
        }
        return biomes[0]; // Retour par défaut
    }

    void CreateTile(int x, int z, float heightValue, BiomePreset biome)
    {
        // Création d'un cube primitif (pas besoin de prefab pour le sol, mais possible d'en mettre un)
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = $"Tile_{x}_{z}";
        tile.transform.parent = tileContainer;

        // Calcul de la position Y (Relief)
        // Pour la mer, on aplatit souvent un peu, sinon on suit le multiplier
        float yPos = heightValue * heightMultiplier;

        // Si c'est la mer, on peut forcer une hauteur fixe pour l'eau ou la laisser vague
        if (biome.name == "Sea")
        {
            yPos = 0.3f * heightMultiplier; // Niveau de la mer un peu plus bas
        }

        tile.transform.position = new Vector3(x, Mathf.Floor(yPos), z); // Mathf.Floor pour un effet "Minecraft" / Voxel

        // Appliquer la couleur du biome
        Renderer rend = tile.GetComponent<Renderer>();
        rend.material.color = biome.color;

        // 5. Faire apparaître des objets (Arbres, Rochers)
        SpawnProps(tile.transform, biome);
    }

    void SpawnProps(Transform parentTile, BiomePreset biome)
    {
        if (biome.props.Length == 0) return;

        // Chance de spawn basée sur la densité du biome (0.0 à 1.0)
        if (Random.value < biome.propDensity)
        {
            GameObject propPrefab = biome.props[Random.Range(0, biome.props.Length)];
            if (propPrefab != null)
            {
                GameObject prop = Instantiate(propPrefab, parentTile.position + Vector3.up, Quaternion.identity);
                prop.transform.parent = parentTile;
                // Légère variation de taille et rotation pour le naturel
                prop.transform.localScale = Vector3.one * Random.Range(0.8f, 1.2f);
                prop.transform.Rotate(0, Random.Range(0, 360), 0);
            }
        }
    }
}

[System.Serializable]
public struct BiomePreset
{
    public string name;
    public Color color;
    [Range(0, 1)] public float propDensity; // Chance qu'un objet apparaisse sur un cube
    public GameObject[] props; // Liste des prefabs (Arbres, Rochers, etc.)
}