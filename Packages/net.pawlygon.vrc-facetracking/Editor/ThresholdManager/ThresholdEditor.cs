using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System;

public class ThresholdEditor : EditorWindow
{
    private AnimatorController animatorController;
    private string paramNamePrefix = "OSCm/Proxy/FT/v2/";
    private List<string> ignoredLayers = new List<string> { "_OSCmooth_Gen" };
    private List<string> predefinedParams = new List<string>
    {
        "EyeLeftX", "EyeRightX", "EyeY", "BrowExpressionLeft", "BrowExpressionRight",
        "EyeLidLeft", "EyeLidRight", "EyeSquintLeft", "EyeSquintRight", "JawOpen",
        "MouthClosed", "MouthUpperUpLeft", "MouthUpperUpRight", "MouthLowerDown",
        "SmileFrownLeft", "SmileFrownRight", "CheekPuffSuckLeft", "CheekPuffSuckRight",
        "LipFunnel", "LipPucker", "MouthX", "NoseSneer", "MouthStretchLeft",
        "MouthStretchRight", "MouthTightenerLeft", "MouthTightenerRight", "LipSuckUpper",
        "LipSuckLower", "JawForward", "JawX", "TongueOut", "TongueX", "TongueY",
        "PupilDilation", "MouthRaiserLower", "MouthRaiserUpper", "MouthPress",
        "MouthTightenerStretchLeft", "MouthTightenerStretchRight", "CheekSquintRight", "CheekSquintLeft"
    };
    private Dictionary<string, List<MotionData>> parameterMotionData = new Dictionary<string, List<MotionData>>();
    private Vector2 scrollPosition;
    private bool changesPending = false;
    private Dictionary<string, bool> foldouts = new Dictionary<string, bool>();
    private string selectedControllerGuid = "9633f793f4e23c645b722b5db7d61098";
    private string unifiedExpressionGuid = "9633f793f4e23c645b722b5db7d61098";
    private string arkitGuid = "c03308fa293ed3541b06d8e7bb5c3260";
    private bool isUnifiedExpressions = true;
    private bool showHelp = true;

    private bool showPopup = false;
    private float popupStartTime;
    private const float popupDuration = 2f;

    [MenuItem("Tools/!Pawlygon/Advanced/Threshold Editor")]
    public static void ShowWindow()
    {
        var window = GetWindow<ThresholdEditor>("Threshold Editor");
        window.minSize = new Vector2(600, 600);
        window.position = new Rect(100, 100, 700, 1000);
        window.LoadSelectedController(); // Load the default controller when the window opens
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(Resources.Load<Texture>("logo"), GUILayout.Width(75), GUILayout.Height(75));
        GUILayout.FlexibleSpace();
        GUILayout.Label("Pawlygon Threshold Editor", new GUIStyle(EditorStyles.boldLabel) { fontSize = 24, margin = new RectOffset(0, 0, 25, 0) });
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
        DrawUILine(Color.grey);

        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Editing Controller: {(isUnifiedExpressions ? "Unified Expressions" : "ARKit")}", new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 });
        if (GUILayout.Button($"Edit {(isUnifiedExpressions ? "ARKit" : "Unified Expressions")} Thresholds", GUILayout.Width(280)))
        {
            isUnifiedExpressions = !isUnifiedExpressions;
            selectedControllerGuid = isUnifiedExpressions ? unifiedExpressionGuid : arkitGuid;
            LoadSelectedController();
        }
        EditorGUILayout.EndHorizontal();
        DrawUILine(Color.grey);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Settings:");
        if (GUILayout.Button(new GUIContent("Reset Defaults", "Reset Thresholds values to default."), GUILayout.Width(150)))
        {
            ResetDefaults(isUnifiedExpressions);
        }
        if (GUILayout.Button(new GUIContent("Export Settings", "Export and make a backup of your current Thresholds."), GUILayout.Width(150)))
        {
            ExportSettings();
        }
        if (GUILayout.Button(new GUIContent("Import Settings", "Import back a previous exported Thresholds"), GUILayout.Width(150)))
        {
            ImportSettings();
        }
        EditorGUILayout.EndHorizontal();
        DrawUILine(Color.grey);

        showHelp = EditorGUILayout.Foldout(showHelp, "Help and Warnings", true, EditorStyles.foldoutHeader);

        if (showHelp)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.HelpBox("This editor allows you to adjust the thresholds for blend trees in the selected controller. You can toggle between 'Unified Expressions' and 'ARKit' controllers using the button above. Please make sure you are modifying the correct Controller.", MessageType.Info);
            EditorGUILayout.HelpBox("Thresholds determine how easily an animation parameter reaches its maximum value. The closer the threshold is to 0, the easier it will be to trigger the maximum animation value. However, this comes at the cost of reduced range. Adjusting thresholds can help if you are having difficulty triggering the maximum animation value. It is not recommended to set thresholds lower than 0.6, as this can excessively reduce the range and responsiveness of the animation. Please proceed with caution when modifying these values.", MessageType.Info);
            EditorGUILayout.HelpBox("This editor is intended for advanced users who are familiar with blend tree thresholds and animation parameters. Making changes here can significantly affect the behavior of your animations. Please proceed with caution and ensure you have a good understanding of what you are modifying. If something doesn't go as planned, you can always reset your settings back to default", MessageType.Warning);
            EditorGUILayout.EndVertical();
        }

        EditorGUILayout.EndVertical();
        DrawUILine(Color.grey);

        if (animatorController != null && parameterMotionData.Count > 0)
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Width(position.width), GUILayout.Height(position.height - 400));
            foreach (var param in predefinedParams)
            {
                if (!parameterMotionData.ContainsKey(param) || parameterMotionData[param].Count == 0) continue;

                if (!foldouts.ContainsKey(param))
                {
                    foldouts[param] = false;
                }

                string displayParamName = param; // Use only the actual parameter name
                foldouts[param] = EditorGUILayout.Foldout(foldouts[param], displayParamName, true, EditorStyles.foldoutHeader);

                if (foldouts[param])
                {
                    EditorGUILayout.BeginVertical("box");
                    DisplayMotions(param);
                    EditorGUILayout.EndVertical();
                    DrawUILine(Color.grey);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        if (showPopup)
        {
            Rect popupRect = new Rect(10, position.height - 40, position.width - 20, 30);
            GUI.Label(popupRect, "Threshold applied", new GUIStyle(EditorStyles.label) 
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.green } // Set text color to green
            });
        }
    }

    private void LoadSelectedController()
    {
        string controllerPath = AssetDatabase.GUIDToAssetPath(selectedControllerGuid);
        animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        CollectMotions();
    }

    private void CollectMotions()
    {
        parameterMotionData.Clear();
        foreach (var param in predefinedParams)
        {
            parameterMotionData[param] = new List<MotionData>();
        }

        if (animatorController != null)
        {
            foreach (var layer in animatorController.layers)
            {
                if (!ignoredLayers.Contains(layer.name))
                {
                    CollectMotions(layer.stateMachine);
                }
            }
        }
    }

    private void CollectMotions(AnimatorStateMachine stateMachine)
    {
        foreach (var state in stateMachine.states)
        {
            if (state.state.motion is BlendTree blendTree)
            {
                CollectMotions(blendTree);
            }
        }

        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            CollectMotions(childStateMachine.stateMachine);
        }
    }

    private void CollectMotions(BlendTree blendTree)
    {
        foreach (var param in predefinedParams)
        {
            string fullParamName = paramNamePrefix + param;

            bool isXParam = blendTree.blendParameter == fullParamName;
            bool isYParam = blendTree.blendParameterY == fullParamName;

            if (isXParam || isYParam)
            {
                var blendTreeSO = new SerializedObject(blendTree);
                var children = blendTreeSO.FindProperty("m_Childs");

                for (int i = 0; i < children.arraySize; i++)
                {
                    SerializedProperty child = children.GetArrayElementAtIndex(i);
                    SerializedProperty threshold = child.FindPropertyRelative("m_Threshold");
                    SerializedProperty position = child.FindPropertyRelative("m_Position");
                    SerializedProperty motion = child.FindPropertyRelative("m_Motion");

                    if (motion != null && motion.objectReferenceValue is AnimationClip animationClip)
                    {
                        var motionData = new MotionData
                        {
                            AnimationClip = animationClip,
                            Threshold = threshold.floatValue,
                            PositionX = position.FindPropertyRelative("x").floatValue,
                            PositionY = position.FindPropertyRelative("y").floatValue,
                            BlendTree = blendTree,
                            IsXParameter = isXParam,
                            IsYParameter = isYParam,
                            Is2D = blendTree.blendType != BlendTreeType.Simple1D
                        };

                        parameterMotionData[param].Add(motionData);
                    }
                }
            }
        }

        foreach (var child in blendTree.children)
        {
            if (child.motion is BlendTree childBlendTree)
            {
                CollectMotions(childBlendTree);
            }
        }
    }

    private void DisplayMotions(string param)
{
    EditorGUILayout.BeginHorizontal();
    EditorGUILayout.LabelField("BlendTree", GUILayout.Width(200));
    EditorGUILayout.LabelField("Motion", GUILayout.Width(200));
    EditorGUILayout.LabelField("Threshold", GUILayout.Width(100));
    EditorGUILayout.EndHorizontal();

    foreach (var motionData in parameterMotionData[param])
    {
        float currentThreshold = motionData.Is2D ? (motionData.IsXParameter ? motionData.PositionX : motionData.PositionY) : motionData.Threshold;

        // Skip displaying if currentThreshold is 0 and it hasn't been edited
        if (currentThreshold == 0 && !motionData.HasBeenEdited)
        {
            continue;
        }

        EditorGUILayout.BeginHorizontal("box");
        try
        {
            EditorGUILayout.LabelField(motionData.BlendTree.name, GUILayout.Width(200));
            EditorGUILayout.LabelField(motionData.AnimationClip.name, GUILayout.Width(200));

            string thresholdValue = currentThreshold.ToString();
            GUI.SetNextControlName(param);
            string newThresholdInput = EditorGUILayout.TextField(thresholdValue, GUILayout.Width(100));

            if (float.TryParse(newThresholdInput, out float newThreshold))
            {
                if (newThreshold != currentThreshold)
                {
                    if (newThreshold == 0)
                    {
                        // Revert changes if the value is set to 0
                        if (motionData.Is2D)
                        {
                            if (motionData.IsXParameter)
                            {
                                newThreshold = motionData.PositionX = currentThreshold;
                            }
                            else if (motionData.IsYParameter)
                            {
                                newThreshold = motionData.PositionY = currentThreshold;
                            }
                        }
                        else
                        {
                            newThreshold = motionData.Threshold = currentThreshold;
                        }
                    }
                    else
                    {
                        if (motionData.Is2D)
                        {
                            if (motionData.IsXParameter)
                            {
                                motionData.PositionX = newThreshold;
                            }
                            else if (motionData.IsYParameter)
                            {
                                motionData.PositionY = newThreshold;
                            }
                        }
                        else
                        {
                            motionData.Threshold = newThreshold;
                        }

                        motionData.HasBeenEdited = true;
                        changesPending = true;
                    }
                }
            }

            // Apply changes immediately
            if (motionData.HasBeenEdited)
            {
                ApplyChanges(param);
                motionData.HasBeenEdited = false;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error displaying blend tree thresholds: {e.Message}");
        }
        finally
        {
            EditorGUILayout.EndHorizontal();
        }
    }
}

    private void ApplyChanges(string param)
    {
        foreach (var motionData in parameterMotionData[param])
        {
            var blendTreeSO = new SerializedObject(motionData.BlendTree);
            var children = blendTreeSO.FindProperty("m_Childs");

            for (int i = 0; i < children.arraySize; i++)
            {
                SerializedProperty child = children.GetArrayElementAtIndex(i);
                SerializedProperty motion = child.FindPropertyRelative("m_Motion");

                if (motion != null && motion.objectReferenceValue == motionData.AnimationClip)
                {
                    if (motionData.Is2D)
                    {
                        var position = child.FindPropertyRelative("m_Position");
                        if (motionData.IsXParameter)
                        {
                            position.FindPropertyRelative("x").floatValue = motionData.PositionX;
                        }
                        else if (motionData.IsYParameter)
                        {
                            position.FindPropertyRelative("y").floatValue = motionData.PositionY;
                        }
                    }
                    else
                    {
                        child.FindPropertyRelative("m_Threshold").floatValue = motionData.Threshold;
                    }

                    Debug.Log($"Applying to {motionData.BlendTree.name} - {motionData.AnimationClip.name}: Threshold={motionData.Threshold}, PosX={motionData.PositionX}, PosY={motionData.PositionY}");
                }
            }

            blendTreeSO.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"Properties applied for blend tree: {motionData.BlendTree.name}");

            // Show popup
            ShowPopup();
        }
    }

    private void ShowPopup()
    {
        showPopup = true;
        popupStartTime = (float)EditorApplication.timeSinceStartup;
        EditorApplication.update += UpdatePopup;
        Repaint();
    }

    private void UpdatePopup()
    {
        if (showPopup && (float)EditorApplication.timeSinceStartup - popupStartTime > popupDuration)
        {
            showPopup = false;
            EditorApplication.update -= UpdatePopup;
            Repaint();
        }
    }

    private void ExportSettings()
    {
        string path = EditorUtility.SaveFilePanel("Export Settings", "", "ThresholdSettings.json", "json");
        if (string.IsNullOrEmpty(path)) return;

        var settings = new ThresholdSettings();
        foreach (var param in parameterMotionData.Keys)
        {
            foreach (var motionData in parameterMotionData[param])
            {
                settings.motionData.Add(new MotionDataSerialized
                {
                    Param = param,
                    Threshold = motionData.Threshold,
                    PositionX = motionData.PositionX,
                    PositionY = motionData.PositionY,
                    IsXParameter = motionData.IsXParameter,
                    IsYParameter = motionData.IsYParameter,
                    Is2D = motionData.Is2D,
                    AnimationClipName = motionData.AnimationClip.name,
                    BlendTreeName = motionData.BlendTree.name
                });
            }
        }

        string json = JsonUtility.ToJson(settings, true);
        File.WriteAllText(path, json);
        Debug.Log("Settings exported to " + path);
    }

    private void ImportSettings()
    {
        string path = EditorUtility.OpenFilePanel("Import Settings", "", "json");
        if (string.IsNullOrEmpty(path)) return;

        string json = File.ReadAllText(path);
        var settings = JsonUtility.FromJson<ThresholdSettings>(json);

        foreach (var data in settings.motionData)
        {
            if (parameterMotionData.ContainsKey(data.Param))
            {
                foreach (var motionData in parameterMotionData[data.Param])
                {
                    if (motionData.AnimationClip.name == data.AnimationClipName && motionData.BlendTree.name == data.BlendTreeName)
                    {
                        motionData.Threshold = data.Threshold;
                        motionData.PositionX = data.PositionX;
                        motionData.PositionY = data.PositionY;
                        motionData.IsXParameter = data.IsXParameter;
                        motionData.IsYParameter = data.IsYParameter;
                        motionData.Is2D = data.Is2D;
                        motionData.HasBeenEdited = true;
                    }
                }
            }
        }
        Repaint();
        Debug.Log("Settings imported from " + path);
    }

    private void ResetDefaults(bool isUnifiedExpressions)
    {
        string path = isUnifiedExpressions ? "Packages/net.pawlygon.vrc-facetracking/Editor/ThresholdManager/Settings/UnifiedExpressionsDefault.json" : "Packages/net.pawlygon.vrc-facetracking/Editor/ThresholdManager/Settings/ARKitDefault.json";
        if (string.IsNullOrEmpty(path)) return;

        string json = File.ReadAllText(path);
        var settings = JsonUtility.FromJson<ThresholdSettings>(json);

        foreach (var data in settings.motionData)
        {
            if (parameterMotionData.ContainsKey(data.Param))
            {
                foreach (var motionData in parameterMotionData[data.Param])
                {
                    if (motionData.AnimationClip.name == data.AnimationClipName && motionData.BlendTree.name == data.BlendTreeName)
                    {
                        motionData.Threshold = data.Threshold;
                        motionData.PositionX = data.PositionX;
                        motionData.PositionY = data.PositionY;
                        motionData.IsXParameter = data.IsXParameter;
                        motionData.IsYParameter = data.IsYParameter;
                        motionData.Is2D = data.Is2D;
                        motionData.HasBeenEdited = true;
                    }
                }
            }
        }
        Repaint();
        Debug.Log("Settings reset to default from " + path);
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

    [System.Serializable]
    private class ThresholdSettings
    {
        public List<MotionDataSerialized> motionData = new List<MotionDataSerialized>();
    }

    [System.Serializable]
    private class MotionDataSerialized
    {
        public string Param;
        public float Threshold;
        public float PositionX;
        public float PositionY;
        public bool IsXParameter;
        public bool IsYParameter;
        public bool Is2D;
        public string AnimationClipName;
        public string BlendTreeName;
    }

    private class MotionData
    {
        public AnimationClip AnimationClip;
        public float Threshold;
        public float PositionX;
        public float PositionY;
        public BlendTree BlendTree;
        public bool IsXParameter;
        public bool IsYParameter;
        public bool Is2D;
        public bool HasBeenEdited;
    }
}
