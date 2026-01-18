using UnityEditor;
using UnityEngine;

namespace UnitySkills.Editor
{
    /// <summary>
    /// Main Editor window for UnitySkills.
    /// Provides UI to start/stop the REST server and view status.
    /// </summary>
    public class UnitySkillsWindow : EditorWindow
    {
        private Vector2 _scrollPos;

        [MenuItem("Window/UnitySkills")]
        public static void ShowWindow()
        {
            var window = GetWindow<UnitySkillsWindow>("UnitySkills");
            window.minSize = new Vector2(300, 200);
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            // Header
            GUILayout.Space(10);
            GUILayout.Label("UnitySkills", EditorStyles.boldLabel);
            GUILayout.Label("REST API for AI Control", EditorStyles.miniLabel);
            GUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Server URL");
            EditorGUILayout.SelectableLabel(SkillsHttpServer.Url, EditorStyles.textField, GUILayout.Height(18));
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // Status
            var statusStyle = new GUIStyle(EditorStyles.label);
            if (SkillsHttpServer.IsRunning)
            {
                statusStyle.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                GUILayout.Label("● Server Running", statusStyle);
            }
            else
            {
                statusStyle.normal.textColor = new Color(0.8f, 0.3f, 0.3f);
                GUILayout.Label("○ Server Stopped", statusStyle);
            }

            GUILayout.Space(10);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !SkillsHttpServer.IsRunning;
            if (GUILayout.Button("Start Server", GUILayout.Height(30)))
            {
                SkillsHttpServer.Start();
            }
            GUI.enabled = SkillsHttpServer.IsRunning;
            if (GUILayout.Button("Stop Server", GUILayout.Height(30)))
            {
                SkillsHttpServer.Stop();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(20);

            // Skills list
            GUILayout.Label("Available Skills", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "• create_cube\n" +
                "• create_sphere\n" +
                "• set_object_color\n" +
                "• get_scene_info\n" +
                "• find_objects_by_tag\n" +
                "• delete_object",
                MessageType.Info);

            GUILayout.Space(20);

            // Quick test
            GUILayout.Label("Quick Test", EditorStyles.boldLabel);
            if (GUILayout.Button("Open Skills Endpoint in Browser"))
            {
                Application.OpenURL(SkillsHttpServer.Url + "skills");
            }

            GUILayout.Space(10);

            // Python usage
            EditorGUILayout.HelpBox(
                "Python Usage:\n" +
                "import unity_skills\n" +
                "unity_skills.create_cube(x=0, y=1, z=0)",
                MessageType.None);

            EditorGUILayout.EndScrollView();
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
