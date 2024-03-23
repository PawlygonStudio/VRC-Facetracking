using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PrefabInstallerWindow : EditorWindow
{
    private string prefabPath = "Packages/net.pawlygon.vrc-facetracking/Prefabs/!Pawlygon - Facetracking Controller.prefab";
    private GameObject selectedObject;
    private Texture2D logoTexture;
    private string logoPath = "Packages/net.pawlygon.vrc-facetracking/Editor/Resources/logo.png";
    bool isValidSelection = true;

    [MenuItem("Tools/!Pawlygon/Face Tracking Template/Installer")]
    public static void ShowWindow()
    {
        var window = GetWindow<PrefabInstallerWindow>("Pawlygon - Face Tracking Installer");
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
        GUILayout.Label("Face Tracking Installer", titleStyle, GUILayout.Height(logoHeight));

        GUILayout.FlexibleSpace(); // Pushes everything before it towards the horizontal center
        GUILayout.EndHorizontal();

        DrawUILine(Color.grey); 

        // Description
        EditorGUILayout.LabelField("Select the avatar you wish to add the Face Tracking Prefab.\nIt needs to have a Mesh Called Body in it.", GUILayout.Height(50));
        DrawUILine(Color.grey);

        // Object Selector
        EditorGUI.BeginChangeCheck();
        selectedObject = (GameObject)EditorGUILayout.ObjectField("Select Avatar", selectedObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck()) // If the user has selected a different object
        {
            isValidSelection = IsFBXObject(selectedObject);  // Check if the selected object is valid
        }

        if (!isValidSelection)
        {
            // Display a warning label if the selection is not valid
            EditorGUILayout.HelpBox("Selected Avatar is not a valid FBX object or does not have a 'Body' Skinned Mesh Renderer.", MessageType.Warning);
        }

        DrawUILine(Color.grey); // Draw line after object selector

        // Assuming isValidSelection is true when the selected object passes the checks
        if (GUILayout.Button("Install") && isValidSelection && selectedObject != null)
        {
            // Load the prefab from the specified path
            GameObject prefabToInstantiate = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabToInstantiate != null)
            {
                // Instantiate the prefab as a child of the selected object
                GameObject instantiatedPrefab = (GameObject)PrefabUtility.InstantiatePrefab(prefabToInstantiate, selectedObject.transform);
                instantiatedPrefab.transform.localPosition = Vector3.zero; // Reset local position if needed
                instantiatedPrefab.transform.localRotation = Quaternion.identity; // Reset local rotation if needed
                instantiatedPrefab.transform.localScale = Vector3.one; // Reset local scale if needed

                Debug.Log("Prefab installed as a child of the selected object.");
            }
            else
            {
                Debug.LogError("Prefab could not be loaded from path: " + prefabPath);
            }
        }
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
