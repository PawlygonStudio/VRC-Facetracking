using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrefabInstallerWindow : EditorWindow
{
    //todo - Find better solution instead of using hardcoded path. Also in future we should handle multiple prefabs.
    private string prefabPath = "Packages/net.pawlygon.vrc-facetracking/Prefabs/!Pawlygon - Facetracking Controller.prefab";
    private GameObject selectedObject;
    private Texture2D logoTexture;
    private string logoPath = "Packages/net.pawlygon.vrc-facetracking/Editor/Resources/logo.png";
    private bool isSelectedObjectValid = false;
    private bool hasValidChild = false;
    private GameObject selectedChild;
    private List<GameObject> childMeshRenderers = new List<GameObject>();
    private string[] childMeshRendererNames = new string[0];
    private int selectedChildIndex = 0;

    [MenuItem("Tools/!Pawlygon/Face Tracking Template/Installer")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabInstallerWindow>("Pawlygon - Template Installer");
        window.LoadLogo();
    }

    private void LoadLogo()
    {
        logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(logoPath);
        if (logoTexture == null)
        {
            Debug.LogWarning("Failed to load logo at path: " + logoPath);
        }
    }

    void OnEnable()
    {
        // Set the minimum size of the window
        this.minSize = new Vector2(350, 300);
    }
    void OnGUI()
    {
        float logoHeight = 50; 
        float logoWidth = 50;
        GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft 
        };

        // Begin a horizontal group for the logo and title
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace(); // Pushes everything after it towards the horizontal center

        // Draw the logo, if it exists
        if (logoTexture != null)
        {
            GUILayout.Label(logoTexture, GUILayout.Width(logoWidth), GUILayout.Height(logoHeight));
        }

        // Draw the title next to the logo
        GUILayout.Label("Template Installer", titleStyle, GUILayout.Height(logoHeight));

        GUILayout.FlexibleSpace(); // Pushes everything before it towards the horizontal center
        GUILayout.EndHorizontal();

        DrawUILine(Color.grey); 

        // Description
        EditorGUILayout.LabelField("Select the avatar you wish to add the Face Tracking Prefab.", GUILayout.Height(50));
        DrawUILine(Color.grey);

        // Object Selector
        EditorGUI.BeginChangeCheck();
        selectedObject = (GameObject)EditorGUILayout.ObjectField("Select Avatar", selectedObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            isSelectedObjectValid = selectedObject != null && IsObjectFBXAndParent(selectedObject);
            childMeshRenderers = selectedObject != null ? GetChildMeshRenderers(selectedObject) : new List<GameObject>();
            childMeshRendererNames = childMeshRenderers.Select(obj => obj.name).ToArray();
            hasValidChild = childMeshRenderers.Any(obj => obj.name == "Body");
            selectedChildIndex = 0; // Reset the child selection
        }

        // Message: Check if the selected object is an FBX and a parent
        if (isSelectedObjectValid)
        {
            EditorGUILayout.HelpBox("The selected avatar is a valid FBX.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("The selected avatar must be an FBX and a parent.", MessageType.Error);
        }

        // Message and dropdown: Check if the selected object has a child FBX called "Body"
        if (!hasValidChild)
        {
            EditorGUILayout.HelpBox("The selected avatar does not have a expected mesh renderer called 'Body'. Please select a child mesh renderer that contains the facetracking blendshapes.", MessageType.Warning);
            if (childMeshRendererNames.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                selectedChildIndex = EditorGUILayout.Popup("Select Child Mesh Renderer", selectedChildIndex, childMeshRendererNames);
                if (EditorGUI.EndChangeCheck() && selectedChildIndex >= 0)
                {
                    selectedChild = childMeshRenderers[selectedChildIndex];
                    // Additional checks or actions based on the selected child can go here
                }
            }
        }

        // Install Button (conditionally enabled based on checks)
        EditorGUI.BeginDisabledGroup(!isSelectedObjectValid || !hasValidChild);
        if (GUILayout.Button("Install"))
        {
            InstallPrefab(selectedObject, prefabPath);
        }
        EditorGUI.EndDisabledGroup();
    }

    // Method to check if the GameObject is an FBX with a Skinned Mesh Renderer child called "Body"
    private bool IsFBXObject(GameObject obj)
    {
        if (obj == null)
            return false;

        // Check if the GameObject is a root object (which might be an FBX)
        if (obj.transform.parent != null)
            return false;

        // Check for a child with a Skinned Mesh Renderer named "Body"
        var bodyRenderer = obj.transform.Find("Body")?.GetComponent<SkinnedMeshRenderer>();
        return bodyRenderer != null;
    }

    private bool IsObjectFBXAndParent(GameObject obj)
    {
        if (obj == null)
        {
            return false;
        }

        // Check if the GameObject has no parent (is a root object)
        bool isRootObject = obj.transform.parent == null;
        Debug.Log(isRootObject);

        // Check if the GameObject has at least one child
        bool hasChildren = obj.transform.childCount > 0;
        Debug.Log(hasChildren);

        // Optional: Check if the GameObject has a MeshFilter or SkinnedMeshRenderer,
        // which are usually present on FBX models
        bool hasMeshComponents = obj.GetComponentsInChildren<MeshFilter>() != null || obj.GetComponentsInChildren<SkinnedMeshRenderer>() != null;
        Debug.Log(hasMeshComponents);

        // The object is considered a valid FBX and a parent if it's a root object, has children, and has mesh components
        return isRootObject && hasChildren && hasMeshComponents;
    }

    private List<GameObject> GetChildMeshRenderers(GameObject obj)
    {
        if (obj == null)
        {
            return new List<GameObject>(); // Return an empty list if input is null
        }

        return obj.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Select(renderer => renderer.gameObject)
                .ToList();
    }

    private void InstallPrefab(GameObject selectedObject, string prefabPath)
    {
        GameObject prefabToInstantiate = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefabToInstantiate != null)
        {
            // Instantiate the prefab as a child of the selected object
            GameObject instantiatedPrefab = (GameObject)PrefabUtility.InstantiatePrefab(prefabToInstantiate, selectedObject.transform);
            instantiatedPrefab.transform.localPosition = Vector3.zero; // Reset local position if needed
            instantiatedPrefab.transform.localRotation = Quaternion.identity; // Reset local rotation if needed
            instantiatedPrefab.transform.localScale = Vector3.one; // Reset local scale if needed

            Debug.Log("Prefab successfully installed to the selected object.");
        }
        else
        {
            Debug.LogError("Prefab could not be loaded from path: " + prefabPath);
        }
    }

    private void DrawUILine(Color color, int thickness = 1, int padding = 10)
    {
        Rect r = EditorGUILayout.GetControlRect(GUILayout.Height(padding + thickness));
        r.height = thickness;
        r.y += padding / 2;
        r.x -= 2;
        r.width += 6;
        EditorGUI.DrawRect(r, color);
    }
}
