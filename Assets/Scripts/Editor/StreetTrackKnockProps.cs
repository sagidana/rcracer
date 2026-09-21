using System;
using System.Collections.Generic;
using UnityEngine;

// Builds the small props (cones, trash cans, mailboxes) as separate, knockable objects.
public static partial class StreetTrackBuilder
{
    // mass of the small props: the car weighs 20 (see CarController > Body)
    public const string PropsParentName = "Props";

    class PropPart
    {
        public string key;
        public Action<MeshData> build;
        public PropPart(string key, Action<MeshData> build) { this.key = key; this.build = build; }
    }

    static Transform propsParent;
    static Dictionary<string, Mesh> propMeshes;
    static Dictionary<string, Material> propMaterials;

    static void BeginProps()
    {
        propMeshes = new Dictionary<string, Mesh>();
        propMaterials = new Dictionary<string, Material>();
        GameObject go = new GameObject(PropsParentName);
        go.transform.SetParent(root, false);
        propsParent = go.transform;
    }

    static Material PropMaterial(string key)
    {
        Material m;
        if (!propMaterials.TryGetValue(key, out m))
        {
            Color c;
            if (!Palette.TryGetValue(key, out c)) c = Color.magenta;
            m = MakeMat(c);
            propMaterials[key] = m;
        }
        return m;
    }

    // One prop = one object with a box collider, a rigidbody and KnockableProp. Meshes and materials are shared.
    static void SpawnProp(string kind, Vector3 pos, Quaternion rot, float mass, Vector3 colliderCenter, Vector3 colliderSize, params PropPart[] parts)
    {
        GameObject go = new GameObject(kind);
        go.transform.SetParent(propsParent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;

        foreach (PropPart part in parts)
        {
            string meshKey = kind + "/" + part.key;
            Mesh mesh;
            if (!propMeshes.TryGetValue(meshKey, out mesh))
            {
                MeshData md = new MeshData();
                part.build(md);
                mesh = md.ToMesh(meshKey);
                propMeshes[meshKey] = mesh;
            }
            GameObject child = new GameObject(part.key);
            child.transform.SetParent(go.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = PropMaterial(part.key);
        }

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.center = colliderCenter;
        bc.size = colliderSize;
        PropPhysicsTool.MakeKnockable(go, mass);
    }
}
