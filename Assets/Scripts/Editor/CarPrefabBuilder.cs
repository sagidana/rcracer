using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Cars > Build Car Prefabs : makes Assets/Resources/Cars/<Name>.prefab for every car in the list.
// Each prefab = a copy of the scene's "Car" (so it keeps the tuning) + a 3D model from the vehicle pack,
// fitted by CarModelSwitcher, plus a few per-car tuning differences (see Specs).
// Safe to run again: existing prefabs are overwritten.
public static class CarPrefabBuilder
{
    const string Folder = "Assets/Resources/Cars";
    const string MaterialFolder = "Assets/Materials/Cars";

    class Spec
    {
        public string name;
        public string modelPath;
        public System.Action<CarController> tune;
        public Spec(string name, string modelPath, System.Action<CarController> tune) { this.name = name; this.modelPath = modelPath; this.tune = tune; }
    }

    // =====================================================================
    //   EDIT HERE - הרכבים שיש לבחור מהם בתפריט
    // =====================================================================
    static readonly Spec[] Specs =
    {
        new Spec("Buggy", "Assets/Free Adventure Vehicles/Prefabs/Vehicle19.prefab", cc =>
        {
            // the all-rounder: default tuning
        }),
        new Spec("Roadster", "Assets/Free Adventure Vehicles/Prefabs/Vehicle16.prefab", cc =>
        {
            // faster and grippier on the road, harder to drift
            cc.drive.maxSpeed *= 1.15f;
            cc.drive.acceleration *= 1.1f;
            cc.grip.friction *= 1.08f;
            cc.grip.driftGripRear = Mathf.Min(1f, cc.grip.driftGripRear + 0.1f);
        }),
        new Spec("Bike", "Assets/Free Adventure Vehicles/Prefabs/Vehicle14.prefab", cc =>
        {
            // light and nimble, slides more
            cc.body.mass *= 0.8f;
            cc.steering.maxSteerAngle += 4f;
            cc.grip.driftGripRear = Mathf.Max(0.2f, cc.grip.driftGripRear - 0.1f);
        }),
    };

    [MenuItem("Tools/Cars/Build Car Prefabs")]
    public static void Build()
    {
        GameObject template = GameObject.Find("Car");
        if (template == null)
        {
            Debug.LogError("No 'Car' in the open scene to use as the template. Open SampleScene (or run Tools > Setup Race Scene) first.");
            return;
        }
        Directory.CreateDirectory(Folder);

        foreach (Spec s in Specs)
        {
            GameObject car = Object.Instantiate(template);
            car.name = s.name;
            car.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            if (!CarModelSwitcher.Apply(car, s.modelPath))
            {
                Object.DestroyImmediate(car);
                continue;
            }
            CarController cc = car.GetComponent<CarController>();
            if (cc != null) s.tune(cc);
            SaveMaterials(car);

            string path = Folder + "/" + s.name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(car, path);
            Object.DestroyImmediate(car);
            Debug.Log("Car prefab saved: " + path);
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    // The model switcher makes URP copies of the pack's materials in memory. A prefab can only reference
    // materials that exist as assets, so save them (once per material name) and point the renderers at the assets.
    static void SaveMaterials(GameObject car)
    {
        Directory.CreateDirectory(MaterialFolder);
        foreach (Renderer r in car.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || AssetDatabase.Contains(m)) continue;
                string file = MaterialFolder + "/" + m.name.Replace(" (URP)", "").Replace("/", "_") + ".mat";
                Material existing = AssetDatabase.LoadAssetAtPath<Material>(file);
                if (existing == null)
                {
                    AssetDatabase.CreateAsset(m, file);
                    existing = m;
                }
                mats[i] = existing;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}
